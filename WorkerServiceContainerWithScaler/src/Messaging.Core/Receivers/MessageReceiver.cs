namespace Messaging.Core.Receivers;

using Azure.Messaging.ServiceBus;
using Messaging.Core.Abstractions;
using Messaging.Core.Audit.Models;
using Messaging.Core.Models;
using Messaging.Core.Options;
using Microsoft.Extensions.Logging;

public sealed class MessageReceiver
{
    private readonly ServiceBusClient _client;
    private readonly MessagingOptions _opts;
    private readonly IAuditRepository _audit;
    private readonly ILogger<MessageReceiver> _logger;

    public MessageReceiver(
        ServiceBusClient client,
        MessagingOptions opts,
        IAuditRepository audit,
        ILogger<MessageReceiver> logger)
    {
        _client = client;
        _opts   = opts;
        _audit  = audit;
        _logger = logger;
    }

    /// <summary>
    /// Start receiving from a queue or "topic/subscription" destination.
    /// Returns a Task that runs until cancellationToken is cancelled.
    /// </summary>
    public Task ReceiveAsync<T>(
        string destinationName,
        IMessageHandler<T> handler,
        ReceiveOptions receiveOpts,
        CancellationToken ct) where T : class
    {
        return receiveOpts.ReceiveMode == ReceiveMode.Push
            ? ReceivePushAsync(destinationName, handler, receiveOpts, ct)
            : ReceivePullBatchAsync(destinationName, handler, receiveOpts, ct);
    }

    // ── Push mode ────────────────────────────────────────────────────────────

    private async Task ReceivePushAsync<T>(
        string destinationName,
        IMessageHandler<T> handler,
        ReceiveOptions receiveOpts,
        CancellationToken ct) where T : class
    {
        var (queueOrTopic, subscription) = ParseDestination(destinationName);

        var processorOptions = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = receiveOpts.ProcessingMode == ProcessingMode.Parallel
                                 ? receiveOpts.MaxDegreeOfParallelism
                                 : 1,
            PrefetchCount      = receiveOpts.PrefetchCount,
            AutoCompleteMessages = false
        };

        ServiceBusProcessor processor = subscription is not null
            ? _client.CreateProcessor(queueOrTopic, subscription, processorOptions)
            : _client.CreateProcessor(queueOrTopic, processorOptions);

        processor.ProcessMessageAsync += args => HandleMessageAsync(args.Message, destinationName, handler, args, ct);
        processor.ProcessErrorAsync   += args =>
        {
            _logger.LogError(args.Exception, "Service Bus processor error on {Destination}", destinationName);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(ct);

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }

        await processor.StopProcessingAsync(CancellationToken.None);
        await processor.DisposeAsync();
    }

    private async Task HandleMessageAsync<T>(
        ServiceBusReceivedMessage sbMessage,
        string destinationName,
        IMessageHandler<T> handler,
        ProcessMessageEventArgs args,
        CancellationToken ct) where T : class
    {
        var clientId = sbMessage.ApplicationProperties.TryGetValue("x-messaging-client-id", out var cid)
            ? cid?.ToString() ?? string.Empty
            : string.Empty;

        var auditEntry = await InsertAuditReceivedAsync(sbMessage, destinationName, clientId, ct);

        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? renewalTask = null;
        if (_opts.LockRenewal.Enabled)
            renewalTask = RenewLockLoopAsync(args, _opts.LockRenewal.RenewalBufferSeconds, renewalCts.Token);

        try
        {
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.Processing, null, ct);

            var context = BuildContext<T>(sbMessage, destinationName, clientId, args);
            var payload = DeserializeBody<T>(sbMessage);

            await handler.HandleAsync(payload, context, ct);

            await args.CompleteMessageAsync(sbMessage, CancellationToken.None);
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.Completed, null, CancellationToken.None);
            _logger.LogInformation("Completed message {MessageId}", sbMessage.MessageId);
        }
        catch (ServiceBusException ex) when (
            ex.Reason == ServiceBusFailureReason.MessageLockLost ||
            ex.Reason == ServiceBusFailureReason.SessionLockLost)
        {
            _logger.LogWarning(ex, "Lock lost for message {MessageId}", sbMessage.MessageId);
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.Failed, ex.Message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler failed for message {MessageId} (delivery {Count})",
                sbMessage.MessageId, sbMessage.DeliveryCount);
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.Failed, ex.Message, CancellationToken.None);
            await args.AbandonMessageAsync(sbMessage, cancellationToken: CancellationToken.None);
        }
        finally
        {
            await renewalCts.CancelAsync();
            if (renewalTask is not null)
                await renewalTask.ConfigureAwait(false);
        }
    }

    // ── PullBatch mode ───────────────────────────────────────────────────────

    private async Task ReceivePullBatchAsync<T>(
        string destinationName,
        IMessageHandler<T> handler,
        ReceiveOptions receiveOpts,
        CancellationToken ct) where T : class
    {
        var (queueOrTopic, subscription) = ParseDestination(destinationName);

        var receiverOptions = new ServiceBusReceiverOptions
        {
            PrefetchCount      = receiveOpts.PrefetchCount,
            ReceiveMode        = Azure.Messaging.ServiceBus.ServiceBusReceiveMode.PeekLock
        };

        ServiceBusReceiver receiver = subscription is not null
            ? _client.CreateReceiver(queueOrTopic, subscription, receiverOptions)
            : _client.CreateReceiver(queueOrTopic, receiverOptions);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var messages = await receiver.ReceiveMessagesAsync(
                    maxMessages: receiveOpts.BatchSize,
                    maxWaitTime: receiveOpts.BatchWaitTimeout,
                    cancellationToken: ct);

                if (messages.Count == 0)
                    continue;

                if (receiveOpts.ProcessingMode == ProcessingMode.Sequential)
                {
                    foreach (var msg in messages)
                        await ProcessPullMessageAsync(receiver, msg, destinationName, handler, ct);
                }
                else
                {
                    await Parallel.ForEachAsync(
                        messages,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = receiveOpts.MaxDegreeOfParallelism,
                            CancellationToken      = ct
                        },
                        async (msg, token) =>
                            await ProcessPullMessageAsync(receiver, msg, destinationName, handler, token));
                }
            }
        }
        finally
        {
            await receiver.DisposeAsync();
        }
    }

    private async Task ProcessPullMessageAsync<T>(
        ServiceBusReceiver receiver,
        ServiceBusReceivedMessage sbMessage,
        string destinationName,
        IMessageHandler<T> handler,
        CancellationToken ct) where T : class
    {
        var clientId = sbMessage.ApplicationProperties.TryGetValue("x-messaging-client-id", out var cid)
            ? cid?.ToString() ?? string.Empty
            : string.Empty;

        var auditEntry = await InsertAuditReceivedAsync(sbMessage, destinationName, clientId, ct);

        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? renewalTask = null;
        if (_opts.LockRenewal.Enabled)
            renewalTask = RenewLockLoopPullAsync(receiver, sbMessage, _opts.LockRenewal.RenewalBufferSeconds, renewalCts.Token);

        try
        {
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.Processing, null, ct);

            var context = BuildContextPull(receiver, sbMessage, destinationName, clientId);
            var payload = DeserializeBody<T>(sbMessage);

            await handler.HandleAsync(payload, context, ct);

            await receiver.CompleteMessageAsync(sbMessage, CancellationToken.None);
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.Completed, null, CancellationToken.None);
        }
        catch (ServiceBusException ex) when (
            ex.Reason == ServiceBusFailureReason.MessageLockLost ||
            ex.Reason == ServiceBusFailureReason.SessionLockLost)
        {
            _logger.LogWarning(ex, "Lock lost for message {MessageId}", sbMessage.MessageId);
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.Failed, ex.Message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler failed for message {MessageId}", sbMessage.MessageId);
            await _audit.UpdateStatusAsync(auditEntry.Id, MessageStatus.Failed, ex.Message, CancellationToken.None);
            try { await receiver.AbandonMessageAsync(sbMessage, cancellationToken: CancellationToken.None); }
            catch { /* lock may already be lost */ }
        }
        finally
        {
            await renewalCts.CancelAsync();
            if (renewalTask is not null)
                await renewalTask.ConfigureAwait(false);
        }
    }

    // ── Lock renewal ─────────────────────────────────────────────────────────

    private static async Task RenewLockLoopAsync(
        ProcessMessageEventArgs args,
        int bufferSeconds,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var lockExpiry  = args.Message.LockedUntil;
                var renewAt     = lockExpiry - TimeSpan.FromSeconds(bufferSeconds);
                var delay       = renewAt - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                if (!ct.IsCancellationRequested)
                    await args.RenewMessageLockAsync(args.Message, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task RenewLockLoopPullAsync(
        ServiceBusReceiver receiver,
        ServiceBusReceivedMessage message,
        int bufferSeconds,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var lockExpiry  = message.LockedUntil;
                var renewAt     = lockExpiry - TimeSpan.FromSeconds(bufferSeconds);
                var delay       = renewAt - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                if (!ct.IsCancellationRequested)
                    await receiver.RenewMessageLockAsync(message, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string queueOrTopic, string? subscription) ParseDestination(string destinationName)
    {
        var parts = destinationName.Split('/', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (destinationName, null);
    }

    private MessageContext BuildContext<T>(
        ServiceBusReceivedMessage msg,
        string destinationName,
        string clientId,
        ProcessMessageEventArgs args) where T : class =>
        new()
        {
            MessageId       = msg.MessageId,
            CorrelationId   = msg.CorrelationId,
            ClientId        = clientId,
            DestinationName = destinationName,
            RawMessage      = msg,
            CompleteAsync   = (ct) => args.CompleteMessageAsync(msg, ct),
            DeadLetterAsync = (reason, desc, ct) => args.DeadLetterMessageAsync(msg, reason, desc, ct),
            AbandonAsync    = (delay, ct) => args.AbandonMessageAsync(msg, cancellationToken: ct)
        };

    private MessageContext BuildContextPull(
        ServiceBusReceiver receiver,
        ServiceBusReceivedMessage msg,
        string destinationName,
        string clientId) =>
        new()
        {
            MessageId       = msg.MessageId,
            CorrelationId   = msg.CorrelationId,
            ClientId        = clientId,
            DestinationName = destinationName,
            RawMessage      = msg,
            CompleteAsync   = (ct) => receiver.CompleteMessageAsync(msg, ct),
            DeadLetterAsync = (reason, desc, ct) => receiver.DeadLetterMessageAsync(msg, reason, desc, ct),
            AbandonAsync    = (delay, ct) => receiver.AbandonMessageAsync(msg, cancellationToken: ct)
        };

    private async Task<MessageAuditLog> InsertAuditReceivedAsync(
        ServiceBusReceivedMessage msg,
        string destinationName,
        string clientId,
        CancellationToken ct)
    {
        var isTopicSub = destinationName.Contains('/');
        var entry = new MessageAuditLog
        {
            ClientId        = clientId,
            ServiceName     = _opts.ServiceName,
            HostName        = Environment.MachineName,
            OperationType   = "Receive",
            DestinationType = isTopicSub ? DestinationType.Subscription : DestinationType.Queue,
            DestinationName = destinationName,
            MessageId       = msg.MessageId,
            CorrelationId   = msg.CorrelationId,
            Subject         = msg.Subject,
            Body            = _opts.Audit.LogMessageBody
                ? msg.Body.ToArray()[..Math.Min(msg.Body.ToArray().Length, _opts.Audit.MaxBodyBytesStored)]
                : null,
            IsBodyCompressed = msg.ApplicationProperties.TryGetValue("x-messaging-compressed", out var c) && c is true,
            BodySizeBytes   = (int)msg.Body.ToMemory().Length,
            Status          = MessageStatus.Received,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        };
        await _audit.InsertAsync(entry, ct);
        return entry;
    }

    private static T DeserializeBody<T>(ServiceBusReceivedMessage msg) where T : class
    {
        var json = msg.Body.ToString();
        return System.Text.Json.JsonSerializer.Deserialize<T>(json)
               ?? throw new InvalidOperationException($"Failed to deserialize message {msg.MessageId} as {typeof(T).Name}");
    }
}
