namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Minimal <see cref="DbContext"/> used in unit tests to verify Entity Framework command dead letter
/// behaviour without requiring a real database provider.
/// </summary>
internal sealed class TestCommandDeadLetterDbContext : DbContext, ICommandDeadLetterDbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestCommandDeadLetterDbContext"/> class.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    public TestCommandDeadLetterDbContext(DbContextOptions<TestCommandDeadLetterDbContext> options)
        : base(options) { }

    /// <inheritdoc />
    public DbSet<CommandDeadLetterEntry> CommandDeadLetterEntries => Set<CommandDeadLetterEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyPulseConfiguration(this);
    }
}
