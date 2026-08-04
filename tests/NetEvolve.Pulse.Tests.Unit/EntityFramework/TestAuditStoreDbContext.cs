namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Minimal <see cref="DbContext"/> used in unit tests to verify Entity Framework audit trail
/// behaviour without requiring a real database provider.
/// </summary>
internal sealed class TestAuditStoreDbContext : DbContext, IAuditStoreDbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestAuditStoreDbContext"/> class.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    public TestAuditStoreDbContext(DbContextOptions<TestAuditStoreDbContext> options)
        : base(options) { }

    /// <inheritdoc />
    public DbSet<AuditRecord> AuditEntries => Set<AuditRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyPulseConfiguration(this);
    }
}
