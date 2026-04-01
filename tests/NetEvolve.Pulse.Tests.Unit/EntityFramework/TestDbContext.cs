namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

internal sealed class TestDbContext : DbContext, IOutboxDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options) { }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyConfiguration(OutboxMessageConfigurationFactory.Create(this));
    }
}
