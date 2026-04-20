namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("Redis")]
public sealed class RedisIdempotencyMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddRedisIdempotencyStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => RedisIdempotencyMediatorBuilderExtensions.AddRedisIdempotencyStore(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddRedisIdempotencyStore_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddRedisIdempotencyStore();

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddRedisIdempotencyStore_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddRedisIdempotencyStore());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(RedisIdempotencyStore));
        }
    }

    [Test]
    public async Task AddRedisIdempotencyStore_RegistersValidatorAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddRedisIdempotencyStore());

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IValidateOptions<RedisIdempotencyKeyOptions>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddRedisIdempotencyStore_RegistersConfigurationAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddRedisIdempotencyStore());

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IConfigureOptions<RedisIdempotencyKeyOptions>)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddRedisIdempotencyStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddRedisIdempotencyStore(opts =>
            {
                opts.KeyPrefix = "custom:";
                opts.TimeToLive = TimeSpan.FromHours(48);
                opts.Database = 3;
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<RedisIdempotencyKeyOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.KeyPrefix).IsEqualTo("custom:");
                _ = await Assert.That(options.Value.TimeToLive).IsEqualTo(TimeSpan.FromHours(48));
                _ = await Assert.That(options.Value.Database).IsEqualTo(3);
            }
        }
    }

    [Test]
    public async Task AddRedisIdempotencyStore_DefaultOptions_HaveExpectedValues()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddRedisIdempotencyStore());

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<RedisIdempotencyKeyOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.KeyPrefix).IsEqualTo("pulse:idempotency:");
                _ = await Assert.That(options.Value.TimeToLive).IsEqualTo(TimeSpan.FromHours(24));
                _ = await Assert.That(options.Value.Database).IsEqualTo(-1);
            }
        }
    }

    [Test]
    public async Task AddRedisIdempotencyStore_CalledTwice_RegistersOnlyOneIdempotencyStore()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddRedisIdempotencyStore().AddRedisIdempotencyStore());

        var count = services.Count(d => d.ServiceType == typeof(IIdempotencyStore));

        _ = await Assert.That(count).IsEqualTo(1);
    }
}
