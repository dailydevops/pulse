namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class InMemoryOutboxMessageConfigurationTests
{
    [Test]
    public async Task Constructor_Parameterless_AppliesDefaultOptions()
    {
        var configuration = new InMemoryOutboxMessageConfiguration();
        var modelBuilder = new ModelBuilder();

        _ = modelBuilder.ApplyConfiguration(configuration);
        var entityType = modelBuilder.Model.FindEntityType(typeof(OutboxMessage));

        using (Assert.Multiple())
        {
            _ = await Assert.That(entityType).IsNotNull();
            _ = await Assert.That(entityType!.GetTableName()).IsEqualTo(new OutboxOptions().TableName);
        }
    }

    [Test]
    public async Task Constructor_WithOptions_AppliesConfiguredOptions()
    {
        var options = Options.Create(new OutboxOptions { Schema = "custom", TableName = "CustomOutbox" });
        var configuration = new InMemoryOutboxMessageConfiguration(options);
        var modelBuilder = new ModelBuilder();

        _ = modelBuilder.ApplyConfiguration(configuration);
        var entityType = modelBuilder.Model.FindEntityType(typeof(OutboxMessage));

        using (Assert.Multiple())
        {
            _ = await Assert.That(entityType).IsNotNull();
            _ = await Assert.That(entityType!.GetSchema()).IsEqualTo("custom");
            _ = await Assert.That(entityType.GetTableName()).IsEqualTo("CustomOutbox");
        }
    }
}
