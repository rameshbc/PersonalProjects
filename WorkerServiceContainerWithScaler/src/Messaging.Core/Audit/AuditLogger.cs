namespace Messaging.Core.Audit;

using System.Threading.Channels;
using Messaging.Core.Abstractions;
using Messaging.Core.Audit.Models;
using Messaging.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal sealed class AuditLogger : BackgroundService, IAuditRepository
{
    private readonly IAuditRepository _inner;
    private readonly ILogger<AuditLogger> _logger;

    // Bounded channel: drops oldest if overwhelmed rather than blocking the message path
    private readonly Channel<Func<CancellationToken, Task>> _channel =
        Channel.CreateBounded<Func<CancellationToken, Task>>(
            new BoundedChannelOptions(4096)
            {
                FullMode           = BoundedChannelFullMode.DropOldest,
                SingleReader       = true,
                AllowSynchronousContinuations = false
            });

    public AuditLogger(IAuditRepository inner, ILogger<AuditLogger> logger)
    {
        _inner  = inner;
        _logger = logger;
    }

    public Task InsertAsync(MessageAuditLog entry, CancellationToken ct = default)
    {
        // Must await the DB directly so entry.Id is populated before the caller
        // queues any UpdateStatusAsync calls that reference it.
        return _inner.InsertAsync(entry, ct);
    }

    public Task UpdateStatusAsync(long id, MessageStatus status, string? statusDetail, CancellationToken ct = default)
    {
        _channel.Writer.TryWrite(innerCt => _inner.UpdateStatusAsync(id, status, statusDetail, innerCt));
        return Task.CompletedTask;
    }

    public Task<int> CountPendingAsync(
        string clientId, string destinationName, string? subject, DateTime cutoff, CancellationToken ct = default)
    {
        // Pending check must be synchronous with the publish path — delegate directly
        return _inner.CountPendingAsync(clientId, destinationName, subject, cutoff, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await work(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit write failed — message path not affected.");
            }
        }
    }
}
