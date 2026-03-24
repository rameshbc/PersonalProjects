namespace Messaging.Core.Audit.DbContext;

using Messaging.Core.Audit.Models;
using Messaging.Core.Models;
using Microsoft.EntityFrameworkCore;

public sealed class MessagingAuditDbContext : DbContext
{
    public MessagingAuditDbContext(DbContextOptions<MessagingAuditDbContext> options)
        : base(options) { }

    public DbSet<MessageAuditLog> MessageAuditLogs => Set<MessageAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var audit = modelBuilder.Entity<MessageAuditLog>();

        audit.ToTable("MessageAuditLog");
        audit.HasKey(x => x.Id);

        audit.Property(x => x.Id).UseIdentityColumn();

        audit.Property(x => x.ClientId).HasMaxLength(128).IsRequired();
        audit.Property(x => x.ServiceName).HasMaxLength(256).IsRequired();
        audit.Property(x => x.HostName).HasMaxLength(256).IsRequired();
        audit.Property(x => x.OperationType).HasMaxLength(32).IsRequired();
        audit.Property(x => x.DestinationName).HasMaxLength(260).IsRequired();
        audit.Property(x => x.MessageId).HasMaxLength(128);
        audit.Property(x => x.CorrelationId).HasMaxLength(128);
        audit.Property(x => x.Subject).HasMaxLength(512);
        audit.Property(x => x.StatusDetail).HasMaxLength(1024);

        // Enum → string (human-readable)
        audit.Property(x => x.Status)
             .HasConversion<string>()
             .HasMaxLength(32)
             .IsRequired();

        audit.Property(x => x.DestinationType)
             .HasConversion<string>()
             .HasMaxLength(16)
             .IsRequired();

        audit.Property(x => x.CreatedAt)
             .HasDefaultValueSql("SYSUTCDATETIME()");

        audit.Property(x => x.UpdatedAt)
             .HasDefaultValueSql("SYSUTCDATETIME()");

        // Hot-path index: pending check query
        audit.HasIndex(x => new { x.ClientId, x.DestinationName, x.Subject, x.Status, x.CreatedAt })
             .HasDatabaseName("IX_Audit_PendingCheck")
             .IsDescending(false, false, false, false, true);

        audit.HasIndex(x => new { x.DestinationName, x.Status, x.CreatedAt })
             .HasDatabaseName("IX_Audit_Destination_Status")
             .IsDescending(false, false, true);

        audit.HasIndex(x => x.MessageId)
             .HasDatabaseName("IX_Audit_MessageId");

        audit.HasIndex(x => x.CreatedAt)
             .HasDatabaseName("IX_Audit_CreatedAt")
             .IsDescending(true);
    }
}
