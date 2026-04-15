using Microsoft.EntityFrameworkCore;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Infrastructure.Persistence.Entities;

namespace SharepointDocManager.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for application configuration and audit data.
///
/// Tables:
///   ClientSites  — one row per client; stores SP/SPE IDs, storage backend, delta token.
///   AuditLogs    — append-only event log for all admin and document operations.
///
/// Connection string: configured in appsettings.json → "ConnectionStrings:DefaultConnection".
/// In production, the connection string should reference Azure Key Vault via
/// a managed identity-backed Key Vault reference in App Service configuration.
///
/// EF Migrations:
///   dotnet ef migrations add InitialCreate --project src/SharepointDocManager.Infrastructure
///   dotnet ef database update             --project src/SharepointDocManager.Infrastructure
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ClientSiteRecord> ClientSites { get; set; } = null!;
    public DbSet<AuditLogRecord>   AuditLogs   { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
    {
        // ── ClientSites ───────────────────────────────────────────────────────
        model.Entity<ClientSiteRecord>(e =>
        {
            e.ToTable("ClientSites");
            e.HasKey(x => x.ClientId);
            e.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            e.Property(x => x.TenantId).HasMaxLength(36).IsRequired();
            e.Property(x => x.StorageBackend).HasMaxLength(30).IsRequired();
            e.Property(x => x.SpSiteId).HasMaxLength(500);
            e.Property(x => x.SpDriveId).HasMaxLength(500);
            e.Property(x => x.SpeContainerId).HasMaxLength(500);
            e.Property(x => x.DeltaToken).HasColumnType("nvarchar(max)");
            e.Property(x => x.UpdatedAt).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
        });

        // ── AuditLogs ─────────────────────────────────────────────────────────
        model.Entity<AuditLogRecord>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ActorId).HasMaxLength(200).IsRequired();
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.ClientId).HasMaxLength(100);
            e.Property(x => x.ResourceId).HasMaxLength(500);
            e.Property(x => x.Details).HasColumnType("nvarchar(max)");
            e.Property(x => x.Timestamp).IsRequired();

            // Index for fast per-client audit queries
            e.HasIndex(x => new { x.ClientId, x.Timestamp });
        });
    }
}
