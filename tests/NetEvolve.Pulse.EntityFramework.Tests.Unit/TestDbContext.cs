namespace NetEvolve.Pulse.EntityFramework.Tests.Unit;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

internal sealed class TestDbContext : DbContext, IOutboxDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options) { }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
}
