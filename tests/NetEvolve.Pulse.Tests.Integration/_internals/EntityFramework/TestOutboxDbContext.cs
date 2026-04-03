namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Test DbContext that implements <see cref="IOutboxDbContext"/> for integration testing.
/// </summary>
/// <remarks>
/// Provides a minimal EF Core DbContext for testing Entity Framework outbox functionality.
/// Uses <see cref="ModelBuilderExtensions.ApplyPulseConfiguration{TContext}"/> to apply
/// provider-specific outbox message configuration automatically.
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
        _ = modelBuilder.ApplyPulseConfiguration(this);
    }
}
