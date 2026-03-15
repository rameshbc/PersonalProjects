using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AspireContainerStarter.Infrastructure.SqlServer.Data;

/// <summary>
/// EF Core DbContext for the shared calculations database.
/// Used by both Calc1Worker and Calc2Worker to persist job results.
/// </summary>
public sealed class CalculationDbContext : DbContext
{
    public CalculationDbContext(DbContextOptions<CalculationDbContext> options)
        : base(options) { }

    public DbSet<CalculationResult> CalculationResults => Set<CalculationResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CalculationResult>(entity =>
        {
            entity.ToTable("CalculationResults");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                  .HasDefaultValueSql("newsequentialid()");

            entity.Property(e => e.JobType)      .HasMaxLength(10) .IsRequired();
            entity.Property(e => e.TaxYear)      .HasMaxLength(10) .IsRequired();
            entity.Property(e => e.EntityId)     .HasMaxLength(100).IsRequired();
            entity.Property(e => e.StateCode)    .HasMaxLength(10);
            entity.Property(e => e.Status)       .HasMaxLength(20) .IsRequired();
            entity.Property(e => e.ResultSummary).HasColumnType("nvarchar(max)");

            entity.HasIndex(e => e.JobId).IsUnique();
        });
    }

    /// <summary>
    /// Design-time factory used by <c>dotnet ef</c> tooling.
    /// Points to the local SQL Server container started by Aspire.
    /// </summary>
    public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CalculationDbContext>
    {
        public CalculationDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<CalculationDbContext>()
                .UseSqlServer(
                    "Server=localhost,1433;Database=calculations-db;User ID=sa;Password=Your_password123;TrustServerCertificate=true",
                    sqlOptions =>
                    {
                        sqlOptions.EnableRetryOnFailure(0);
                        sqlOptions.CommandTimeout(60);
                    })
                .Options;

            return new CalculationDbContext(options);
        }
    }
}
