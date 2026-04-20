namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

[TestGroup("Redis")]
public sealed class RedisIdempotencyKeyOptionsConfigurationTests
{
    [Test]
    public async Task Constructor_WithNullConfiguration_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new RedisIdempotencyKeyOptionsConfiguration(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Configure_WithNullOptions_ThrowsArgumentNullException()
    {
        var configuration = new ConfigurationBuilder().Build();
        var config = new RedisIdempotencyKeyOptionsConfiguration(configuration);

        _ = await Assert.That(() => config.Configure(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Configure_WithEmptyConfiguration_LeavesDefaultValues()
    {
        var configuration = new ConfigurationBuilder().Build();
        var config = new RedisIdempotencyKeyOptionsConfiguration(configuration);

        var options = new RedisIdempotencyKeyOptions();
        config.Configure(options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.KeyPrefix).IsEqualTo("pulse:idempotency:");
            _ = await Assert.That(options.TimeToLive).IsEqualTo(TimeSpan.FromHours(24));
            _ = await Assert.That(options.Database).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task Configure_WithSectionValues_OverridesDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Pulse:Idempotency:Redis:KeyPrefix"] = "custom:",
                    ["Pulse:Idempotency:Redis:TimeToLive"] = "12:00:00",
                    ["Pulse:Idempotency:Redis:Database"] = "2",
                }
            )
            .Build();

        var config = new RedisIdempotencyKeyOptionsConfiguration(configuration);
        var options = new RedisIdempotencyKeyOptions();
        config.Configure(options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.KeyPrefix).IsEqualTo("custom:");
            _ = await Assert.That(options.TimeToLive).IsEqualTo(TimeSpan.FromHours(12));
            _ = await Assert.That(options.Database).IsEqualTo(2);
        }
    }
}
