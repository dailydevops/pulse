namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("RabbitMQ")]
public class RabbitMqTransportOptionsConfigurationTests
{
    [Test]
    public async Task Configure_WithPopulatedSection_BindsAllProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Pulse:Transports:RabbitMq:ExchangeName"] = "pulse-exchange",
                    ["Pulse:Transports:RabbitMq:MaxChannelPoolSize"] = "25",
                }
            )
            .Build();

        var configurator = new RabbitMqTransportOptionsConfiguration(configuration);
        var options = new RabbitMqTransportOptions();

        configurator.Configure(options);

        _ = await Assert.That(options.ExchangeName).IsEqualTo("pulse-exchange");
        _ = await Assert.That(options.MaxChannelPoolSize).IsEqualTo(25);
    }

    [Test]
    public async Task Configure_WithoutSection_LeavesDefaultsUnchanged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var configurator = new RabbitMqTransportOptionsConfiguration(configuration);
        var options = new RabbitMqTransportOptions();
        var defaultExchangeName = options.ExchangeName;
        var defaultPoolSize = options.MaxChannelPoolSize;

        configurator.Configure(options);

        _ = await Assert.That(options.ExchangeName).IsEqualTo(defaultExchangeName);
        _ = await Assert.That(options.MaxChannelPoolSize).IsEqualTo(defaultPoolSize);
    }

    [Test]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new RabbitMqTransportOptionsConfiguration(null!));
}
