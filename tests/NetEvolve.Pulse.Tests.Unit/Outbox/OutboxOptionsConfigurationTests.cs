namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("Outbox")]
public class OutboxOptionsConfigurationTests
{
    [Test]
    public async Task Configure_WithPopulatedSection_BindsAllProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Pulse:Outbox:Schema"] = "custom",
                    ["Pulse:Outbox:TableName"] = "CustomOutboxMessage",
                    ["Pulse:Outbox:ConnectionString"] = "Data Source=test.db",
                    ["Pulse:Outbox:EnableWalMode"] = "false",
                    ["Pulse:Outbox:ProcessingLeaseTimeout"] = "00:10:00",
                }
            )
            .Build();

        var configurator = new OutboxOptionsConfiguration(configuration);
        var options = new OutboxOptions();

        configurator.Configure(options);

        _ = await Assert.That(options.Schema).IsEqualTo("custom");
        _ = await Assert.That(options.TableName).IsEqualTo("CustomOutboxMessage");
        _ = await Assert.That(options.ConnectionString).IsEqualTo("Data Source=test.db");
        _ = await Assert.That(options.EnableWalMode).IsFalse();
        _ = await Assert.That(options.ProcessingLeaseTimeout).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task Configure_WithoutSection_LeavesDefaultsUnchanged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var configurator = new OutboxOptionsConfiguration(configuration);
        var options = new OutboxOptions();
        var defaultSchema = options.Schema;
        var defaultTableName = options.TableName;
        var defaultConnectionString = options.ConnectionString;
        var defaultWalMode = options.EnableWalMode;
        var defaultLeaseTimeout = options.ProcessingLeaseTimeout;

        configurator.Configure(options);

        _ = await Assert.That(options.Schema).IsEqualTo(defaultSchema);
        _ = await Assert.That(options.TableName).IsEqualTo(defaultTableName);
        _ = await Assert.That(options.ConnectionString).IsEqualTo(defaultConnectionString);
        _ = await Assert.That(options.EnableWalMode).IsEqualTo(defaultWalMode);
        _ = await Assert.That(options.ProcessingLeaseTimeout).IsEqualTo(defaultLeaseTimeout);
    }

    [Test]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new OutboxOptionsConfiguration(null!));
}
