namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("Redis")]
public class RedisStreamsTransportOptionsConfigurationTests
{
    [Test]
    public async Task Configure_WithPopulatedSection_BindsAllProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Pulse:Transports:RedisStreams:StreamKey"] = "custom:stream",
                    ["Pulse:Transports:RedisStreams:ConsumerGroupName"] = "custom-group",
                    ["Pulse:Transports:RedisStreams:ConsumerName"] = "custom-consumer",
                    ["Pulse:Transports:RedisStreams:Database"] = "3",
                    ["Pulse:Transports:RedisStreams:CreateStreamIfNotExists"] = "false",
                }
            )
            .Build();

        var configurator = new RedisStreamsTransportOptionsConfiguration(configuration);
        var options = new RedisStreamsTransportOptions();

        configurator.Configure(options);

        _ = await Assert.That(options.StreamKey).IsEqualTo("custom:stream");
        _ = await Assert.That(options.ConsumerGroupName).IsEqualTo("custom-group");
        _ = await Assert.That(options.ConsumerName).IsEqualTo("custom-consumer");
        _ = await Assert.That(options.Database).IsEqualTo(3);
        _ = await Assert.That(options.CreateStreamIfNotExists).IsFalse();
    }

    [Test]
    public async Task Configure_WithoutSection_LeavesDefaultsUnchanged()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var configurator = new RedisStreamsTransportOptionsConfiguration(configuration);
        var options = new RedisStreamsTransportOptions();
        var defaultStreamKey = options.StreamKey;
        var defaultConsumerGroupName = options.ConsumerGroupName;
        var defaultConsumerName = options.ConsumerName;
        var defaultDatabase = options.Database;
        var defaultCreateStreamIfNotExists = options.CreateStreamIfNotExists;

        configurator.Configure(options);

        _ = await Assert.That(options.StreamKey).IsEqualTo(defaultStreamKey);
        _ = await Assert.That(options.ConsumerGroupName).IsEqualTo(defaultConsumerGroupName);
        _ = await Assert.That(options.ConsumerName).IsEqualTo(defaultConsumerName);
        _ = await Assert.That(options.Database).IsEqualTo(defaultDatabase);
        _ = await Assert.That(options.CreateStreamIfNotExists).IsEqualTo(defaultCreateStreamIfNotExists);
    }

    [Test]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new RedisStreamsTransportOptionsConfiguration(null!));
}
