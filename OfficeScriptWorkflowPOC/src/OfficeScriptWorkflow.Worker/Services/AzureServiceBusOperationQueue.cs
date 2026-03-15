using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using OfficeScriptWorkflow.Worker.Configuration;

namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Azure Service Bus backed operation queue for multi-replica deployments.
///
/// Design:
/// - Session-enabled queue: SessionId = WorkbookId
///   Guarantees all operations for the same workbook are processed by ONE replica
///   at a time, preventing concurrent write collisions in Excel.
/// - Message body = JSON envelope with discriminator field "operationType".
/// - Extract operations (read) carry only the operation metadata. Results are
///   stored in IOperationResultStore (keyed by OperationId) so the enqueuing
///   caller can await the result regardless of which replica runs it.
/// - Message lock duration on the Service Bus queue MUST exceed Office Script
///   max runtime (300s). Recommended: 360s (6 minutes). Configure this on
///   the Azure resource, not here.
/// </summary>
public sealed class AzureServiceBusOperationQueue : IOperationQueue, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly ServiceBusSessionProcessor _processor;
    private readonly ServiceBusConfiguration _config;
    private readonly ILogger<AzureServiceBusOperationQueue> _logger;

    // Async enumerable consumer: enqueued via Channel, processor writes to it.
    private readonly System.Threading.Channels.Channel<ExcelOperation> _inboundChannel;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AzureServiceBusOperationQueue(
        IOptions<ServiceBusConfiguration> config,
        ILogger<AzureServiceBusOperationQueue> logger)
    {
        _config = config.Value;
        _logger = logger;

        _client = new ServiceBusClient(_config.ConnectionString, new ServiceBusClientOptions
        {
            TransportType = ServiceBusTransportType.AmqpTcp
        });

        _sender = _client.CreateSender(_config.QueueName);

        _processor = _client.CreateSessionProcessor(_config.QueueName,
            new ServiceBusSessionProcessorOptions
            {
                MaxConcurrentSessions = _config.MaxConcurrentSessions,
                MaxConcurrentCallsPerSession = 1,   // Strict ordering within a session.
                AutoCompleteMessages = false         // We complete manually after dispatch.
            });

        _inboundChannel = System.Threading.Channels.Channel.CreateUnbounded<ExcelOperation>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;
    }

    public async ValueTask EnqueueAsync(ExcelOperation operation, CancellationToken ct = default)
    {
        var envelope = OperationEnvelope.From(operation);
        var body = JsonSerializer.Serialize(envelope, _jsonOptions);

        var message = new ServiceBusMessage(body)
        {
            SessionId = operation.WorkbookId,          // Routes to session = workbook.
            MessageId = operation.Id.ToString(),
            ContentType = "application/json",
            Subject = envelope.OperationType
        };

        await _sender.SendMessageAsync(message, ct);

        _logger.LogDebug(
            "Sent {OperationType} to Service Bus. WorkbookId={WorkbookId} MessageId={MessageId}",
            envelope.OperationType, operation.WorkbookId, message.MessageId);
    }

    public async IAsyncEnumerable<ExcelOperation> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await _processor.StartProcessingAsync(ct);
        _logger.LogInformation("Service Bus session processor started. Queue: {Queue}", _config.QueueName);

        await foreach (var op in _inboundChannel.Reader.ReadAllAsync(ct))
            yield return op;
    }

    private async Task OnMessageAsync(ProcessSessionMessageEventArgs args)
    {
        var ct = args.CancellationToken;

        try
        {
            var body = args.Message.Body.ToString();
            var envelope = JsonSerializer.Deserialize<OperationEnvelope>(body, _jsonOptions)
                ?? throw new InvalidOperationException("Null envelope from Service Bus message.");

            var operation = envelope.ToOperation();

            _logger.LogDebug(
                "Received {OperationType} from Service Bus. WorkbookId={WorkbookId} MessageId={MessageId}",
                envelope.OperationType, args.Message.SessionId, args.Message.MessageId);

            await _inboundChannel.Writer.WriteAsync(operation, ct);

            // Complete only after the operation has been accepted into the local channel.
            await args.CompleteMessageAsync(args.Message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to deserialise Service Bus message {MessageId}. Dead-lettering.",
                args.Message.MessageId);

            await args.DeadLetterMessageAsync(args.Message,
                deadLetterReason: "DeserializationFailure",
                deadLetterErrorDescription: ex.Message,
                cancellationToken: ct);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus processor error. Source={Source} EntityPath={EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _processor.StopProcessingAsync();
        await _processor.DisposeAsync();
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}

// ---------- Serialisation envelope ----------

/// <summary>
/// Wire format for Service Bus messages. A discriminated union over all operation types.
/// This avoids polymorphic JSON deserialization issues with System.Text.Json.
/// </summary>
internal sealed class OperationEnvelope
{
    public string OperationType { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string WorkbookId { get; set; } = string.Empty;
    public DateTimeOffset EnqueuedAt { get; set; }

    // InsertRows
    public string? SheetName { get; set; }
    public string? TableName { get; set; }
    public object?[][]? Rows { get; set; }

    // UpdateRange
    public string? RangeAddress { get; set; }
    public object?[][]? Values { get; set; }

    // ExtractRange / ExtractDynamicArray
    public string? AnchorCell { get; set; }

    public static OperationEnvelope From(ExcelOperation op) => op switch
    {
        InsertRowsOperation o => new OperationEnvelope
        {
            OperationType = "InsertRows",
            Id = o.Id, WorkbookId = o.WorkbookId, EnqueuedAt = o.EnqueuedAt,
            SheetName = o.SheetName, TableName = o.TableName, Rows = o.Rows
        },
        UpdateRangeOperation o => new OperationEnvelope
        {
            OperationType = "UpdateRange",
            Id = o.Id, WorkbookId = o.WorkbookId, EnqueuedAt = o.EnqueuedAt,
            SheetName = o.SheetName, RangeAddress = o.RangeAddress, Values = o.Values
        },
        ExtractRangeOperation o => new OperationEnvelope
        {
            OperationType = "ExtractRange",
            Id = o.Id, WorkbookId = o.WorkbookId, EnqueuedAt = o.EnqueuedAt,
            SheetName = o.SheetName, RangeAddress = o.RangeAddress
        },
        ExtractDynamicArrayOperation o => new OperationEnvelope
        {
            OperationType = "ExtractDynamicArray",
            Id = o.Id, WorkbookId = o.WorkbookId, EnqueuedAt = o.EnqueuedAt,
            SheetName = o.SheetName, AnchorCell = o.AnchorCell
        },
        _ => throw new NotSupportedException($"Unknown operation type: {op.GetType().Name}")
    };

    public ExcelOperation ToOperation() => OperationType switch
    {
        "InsertRows" => new InsertRowsOperation(SheetName!, TableName!, Rows ?? [])
            { WorkbookId = WorkbookId, Id = Id, EnqueuedAt = EnqueuedAt },
        "UpdateRange" => new UpdateRangeOperation(SheetName!, RangeAddress!, Values ?? [])
            { WorkbookId = WorkbookId, Id = Id, EnqueuedAt = EnqueuedAt },
        "ExtractRange" => new ExtractRangeOperation(SheetName!, RangeAddress!)
            { WorkbookId = WorkbookId, Id = Id, EnqueuedAt = EnqueuedAt },
        "ExtractDynamicArray" => new ExtractDynamicArrayOperation(SheetName!, AnchorCell!)
            { WorkbookId = WorkbookId, Id = Id, EnqueuedAt = EnqueuedAt },
        _ => throw new NotSupportedException($"Unknown OperationType in envelope: {OperationType}")
    };
}
