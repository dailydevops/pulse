namespace NetEvolve.Pulse.EntityFramework.Tests.Integration.Fixtures;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Test DbContext that implements <see cref="IOutboxDbContext"/> for integration testing.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Provides a minimal EF Core DbContext for testing Entity Framework outbox functionality.
/// Applies the standard <see cref="OutboxMessageConfiguration"/> for schema consistency.
/// </remarks>
public sealed class TestOutboxDbContext : DbContext, IOutboxDbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestOutboxDbContext"/> class.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    public TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options)
        : base(options) { }

    /// <inheritdoc />
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
