namespace Messaging.Core.Audit;

using Messaging.Core.Audit.DbContext;
using Messaging.Core.Models;
using Microsoft.EntityFrameworkCore;

internal static class CompiledQueries
{
    internal static readonly Func<MessagingAuditDbContext, string, string, string, DateTime, Task<int>>
        PendingCountWithSubject = EF.CompileAsyncQuery(
            (MessagingAuditDbContext db, string clientId, string destinationName, string subject, DateTime cutoff) =>
                db.MessageAuditLogs
                  .AsNoTracking()
                  .Count(x =>
                      x.ClientId        == clientId        &&
                      x.DestinationName == destinationName &&
                      x.Subject         == subject         &&
                      (x.Status == MessageStatus.Queued     ||
                       x.Status == MessageStatus.Published  ||
                       x.Status == MessageStatus.Received   ||
                       x.Status == MessageStatus.Processing) &&
                      x.CreatedAt >= cutoff));

    internal static readonly Func<MessagingAuditDbContext, string, string, DateTime, Task<int>>
        PendingCountNoSubject = EF.CompileAsyncQuery(
            (MessagingAuditDbContext db, string clientId, string destinationName, DateTime cutoff) =>
                db.MessageAuditLogs
                  .AsNoTracking()
                  .Count(x =>
                      x.ClientId        == clientId        &&
                      x.DestinationName == destinationName &&
                      (x.Status == MessageStatus.Queued     ||
                       x.Status == MessageStatus.Published  ||
                       x.Status == MessageStatus.Received   ||
                       x.Status == MessageStatus.Processing) &&
                      x.CreatedAt >= cutoff));
}
