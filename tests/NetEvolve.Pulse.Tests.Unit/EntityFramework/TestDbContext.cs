namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Minimal <see cref="DbContext"/> used in unit tests to verify Entity Framework outbox behaviour
/// without requiring a real database provider.
/// </summary>
internal sealed class TestDbContext : DbContext, IOutboxDbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestDbContext"/> class.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options) { }

    /// <inheritdoc />
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyPulseConfiguration(this);
    }
}
