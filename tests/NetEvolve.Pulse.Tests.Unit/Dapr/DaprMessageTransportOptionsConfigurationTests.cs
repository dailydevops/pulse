namespace NetEvolve.Pulse.Tests.Unit.Dapr;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("Dapr")]
public class DaprMessageTransportOptionsConfigurationTests
{
    [Test]
    public async Task Configure_WithPopulatedSection_BindsAllProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Pulse:Transports:Dapr:PubSubName"] = "custom-pubsub" }
            )
            .Build();

        var configurator = new DaprMessageTransportOptionsConfiguration(configuration);
        var options = new DaprMessageTransportOptions();

        configurator.Configure(options);

        _ = await Assert.That(options.PubSubName).IsEqualTo("custom-pubsub");
    }

    [Test]
    public async Task Configure_WithoutSection_LeavesDefaultsUnchanged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var configurator = new DaprMessageTransportOptionsConfiguration(configuration);
        var options = new DaprMessageTransportOptions();
        var defaultPubSubName = options.PubSubName;

        configurator.Configure(options);

        _ = await Assert.That(options.PubSubName).IsEqualTo(defaultPubSubName);
    }

    [Test]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new DaprMessageTransportOptionsConfiguration(null!));
}
