namespace Messaging.Core.Publishers;

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Messaging.Core.Abstractions;
using Messaging.Core.Audit.Models;
using Messaging.Core.Compression;
using Messaging.Core.Models;
using Messaging.Core.Options;
using Messaging.Core.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

internal sealed class MessagePublisher : IMessagePublisher
{
    private readonly ServiceBusClient _client;
    private readonly MessagingOptions _opts;
    private readonly IPayloadCompressor _compressor;
    private readonly IAuditRepository _audit;
    private readonly ILogger<MessagePublisher> _logger;
    private readonly ResiliencePipeline _pipeline;

    public MessagePublisher(
        ServiceBusClient client,
        MessagingOptions opts,
        IPayloadCompressor compressor,
        IAuditRepository audit,
        ILogger<MessagePublisher> logger)
    {
        _client     = client;
        _opts       = opts;
        _compressor = compressor;
        _audit      = audit;
        _logger     = logger;
        _pipeline   = ServiceBusResiliencePipeline.Build(opts, logger);
    }

    public async Task<PublishResult> PublishAsync<T>(
        string destinationName,
        T payload,
        PublishOptions? options = null,
        CancellationToken ct = default) where T : class
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var envelope = new MessageEnvelope
        {
            Body        = body,
            ContentType = "application/json",
            Subject     = typeof(T).Name
        };
        return await PublishAsync(destinationName, envelope, options, ct);
    }

    public async Task<PublishResult> PublishAsync(
        string destinationName,
        MessageEnvelope envelope,
        PublishOptions? options = null,
        CancellationToken ct = default)
    {
        // 1. Pending check — runs BEFORE the audit insert so the count reflects only
        //    messages already committed to DB (not the current one, which hasn't been
        //    inserted yet). This also ensures concurrent publishers see accurate counts.
        int pendingCount = 0;
        if (_opts.Audit.PendingCheck.Enabled)
        {
            var maxPending = options?.MaxPendingBeforeSuppress
                             ?? _opts.Audit.PendingCheck.MaxPendingBeforeSuppress;
            var cutoff = DateTime.UtcNow.AddMinutes(-_opts.Audit.PendingCheck.LookbackWindowMinutes);

            // Count existing in-flight messages (Queued | Published | Received | Processing)
            // for this client + destination + subject combination within the lookback window.
            pendingCount = await _audit.CountPendingAsync(
                envelope.ClientId, destinationName, envelope.Subject, cutoff, ct);

            if (pendingCount >= maxPending)
            {
                var reason = $"Pending count {pendingCount} >= threshold {maxPending} " +
                             $"(clientId={envelope.ClientId}, destination={destinationName}, subject={envelope.Subject ?? "*"})";
                _logger.LogWarning("Suppressing publish to {Destination}: {Reason}", destinationName, reason);

                // Audit the suppression — this can be fire-and-forget
                var suppressedEntry = BuildAuditEntry(destinationName, envelope, MessageStatus.Suppressed);
                suppressedEntry.PendingCount = pendingCount;
                await _audit.InsertAsync(suppressedEntry, ct);

                return PublishResult.Suppressed(envelope.MessageId, pendingCount, reason);
            }
        }

        // 2. Insert Queued audit row (after the check passes)
        var auditEntry = BuildAuditEntry(destinationName, envelope, MessageStatus.Queued);
        auditEntry.PendingCount = pendingCount;
        await _audit.InsertAsync(auditEntry, ct);

        // 3. Compress if needed
        var (body, isCompressed, contentType) = PrepareBody(envelope, options);

        // 4. Build Service Bus message
        var sbMessage = new ServiceBusMessage(body)
        {
            MessageId       = envelope.MessageId,
            CorrelationId   = envelope.CorrelationId,
            Subject         = envelope.Subject,
            ContentType     = contentType,
            SessionId       = envelope.SessionId
        };

        if (envelope.ScheduledEnqueueTime.HasValue)
            sbMessage.ScheduledEnqueueTime = envelope.ScheduledEnqueueTime.Value;

        sbMessage.ApplicationProperties["x-messaging-client-id"]  = envelope.ClientId;
        sbMessage.ApplicationProperties["x-messaging-compressed"]  = isCompressed;

        foreach (var (k, v) in envelope.ApplicationProperties)
            sbMessage.ApplicationProperties[k] = v;

        // 5. Send via resilience pipeline
        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                var sender = _client.CreateSender(destinationName);
                await using var _ = sender.ConfigureAwait(false);
                await sender.SendMessageAsync(sbMessage, token);
            }, ct);

            _logger.LogInformation("Published message {MessageId} to {Destination}", envelope.MessageId, destinationName);
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.Published, null, ct);
            return PublishResult.Success(envelope.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message {MessageId} to {Destination}", envelope.MessageId, destinationName);
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.PublishFailed, ex.Message, ct);
            return PublishResult.Failed(envelope.MessageId, ex);
        }
    }

    private (BinaryData body, bool isCompressed, string contentType) PrepareBody(
        MessageEnvelope envelope, PublishOptions? options)
    {
        var shouldCompress = options?.Compress
            ?? (_opts.EnableCompression && envelope.Body.Length > _opts.CompressionThresholdBytes);

        if (shouldCompress && !envelope.IsCompressed)
        {
            var compressed = _compressor.Compress(envelope.Body.Span);
            return (new BinaryData(compressed), true, "application/json+gzip");
        }

        return (new BinaryData(envelope.Body), envelope.IsCompressed, envelope.ContentType ?? "application/json");
    }

    private MessageAuditLog BuildAuditEntry(string destinationName, MessageEnvelope envelope, MessageStatus status)
    {
        var isTopicSub = destinationName.Contains('/');
        return new MessageAuditLog
        {
            ClientId        = envelope.ClientId,
            ServiceName     = _opts.ServiceName,
            HostName        = Environment.MachineName,
            OperationType   = "Publish",
            DestinationType = isTopicSub ? DestinationType.Topic : DestinationType.Queue,
            DestinationName = destinationName,
            MessageId       = envelope.MessageId,
            CorrelationId   = envelope.CorrelationId,
            Subject         = envelope.Subject,
            Body            = _opts.Audit.LogMessageBody
                ? envelope.Body.ToArray()[..Math.Min(envelope.Body.Length, _opts.Audit.MaxBodyBytesStored)]
                : null,
            IsBodyCompressed = envelope.IsCompressed,
            BodySizeBytes   = envelope.Body.Length,
            Status          = status,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        };
    }
}
