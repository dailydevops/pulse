namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Minimal <see cref="DbContext"/> used in unit tests to verify Entity Framework idempotency behaviour
/// without requiring a real database provider.
/// </summary>
internal sealed class TestIdempotencyDbContext : DbContext, IIdempotencyStoreDbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestIdempotencyDbContext"/> class.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    public TestIdempotencyDbContext(DbContextOptions<TestIdempotencyDbContext> options)
        : base(options) { }

    /// <inheritdoc />
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyPulseConfiguration(this);
    }
}
