namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class InMemoryIdempotencyKeyConfigurationTests
{
    [Test]
    public async Task Constructor_Parameterless_AppliesDefaultOptions()
    {
        var configuration = new InMemoryIdempotencyKeyConfiguration();
        var modelBuilder = new ModelBuilder();

        _ = modelBuilder.ApplyConfiguration(configuration);
        var entityType = modelBuilder.Model.FindEntityType(typeof(IdempotencyKey));

        using (Assert.Multiple())
        {
            _ = await Assert.That(entityType).IsNotNull();
            _ = await Assert.That(entityType!.GetTableName()).IsEqualTo(new IdempotencyKeyOptions().TableName);
        }
    }

    [Test]
    public async Task Constructor_WithOptions_AppliesConfiguredOptions()
    {
        var options = Options.Create(new IdempotencyKeyOptions { Schema = "custom", TableName = "CustomKeys" });
        var configuration = new InMemoryIdempotencyKeyConfiguration(options);
        var modelBuilder = new ModelBuilder();

        _ = modelBuilder.ApplyConfiguration(configuration);
        var entityType = modelBuilder.Model.FindEntityType(typeof(IdempotencyKey));

        using (Assert.Multiple())
        {
            _ = await Assert.That(entityType).IsNotNull();
            _ = await Assert.That(entityType!.GetSchema()).IsEqualTo("custom");
            _ = await Assert.That(entityType.GetTableName()).IsEqualTo("CustomKeys");
        }
    }
}
