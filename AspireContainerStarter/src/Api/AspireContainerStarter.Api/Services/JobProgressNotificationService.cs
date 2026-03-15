using AspireContainerStarter.Api.Hubs;
using AspireContainerStarter.Contracts.Messages;
using AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace AspireContainerStarter.Api.Services;

/// <summary>
/// Service Bus consumer that receives <see cref="JobProgressMessage"/> events
/// published by workers and forwards them to connected SignalR clients.
///
/// Registered via <c>AddAzureServiceBusConsumerWithManagedIdentity</c> in Program.cs.
/// The <see cref="ServiceBusProcessorHostedService{T}"/> hosted service starts
/// the processor and dispatches messages here in a new DI scope per message.
/// </summary>
internal sealed class JobProgressNotificationService : IMessageConsumer<JobProgressMessage>
{
    private readonly IHubContext<JobProgressHub> _hubContext;
    private readonly ILogger<JobProgressNotificationService> _logger;

    public JobProgressNotificationService(
        IHubContext<JobProgressHub> hubContext,
        ILogger<JobProgressNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger     = logger;
    }

    public async Task HandleAsync(
        JobProgressMessage message,
        string messageId,
        string? correlationId,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Job {JobId} [{JobType}] — {Status} {Percent}%",
            message.JobId, message.JobType, message.Status, message.PercentComplete);

        // Push to all clients subscribed to this job's group.
        await _hubContext
            .Clients
            .Group(JobProgressHub.GroupName(message.JobId.ToString()))
            .SendAsync("ReceiveProgress", message, ct);
    }
}
