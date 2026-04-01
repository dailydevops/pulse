namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

internal sealed class TestDbContext : DbContext, IOutboxDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options) { }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<Type>().HaveConversion<TypeValueConverter>();
    }
}
