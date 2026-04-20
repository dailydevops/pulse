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
    public async Task AddRedisIdempotencyStore_WithoutConfigure_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddRedisIdempotencyStore();

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddRedisIdempotencyStore_WithoutConfigure_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddRedisIdempotencyStore());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(RedisIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddRedisIdempotencyStore_WithoutConfigure_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddRedisIdempotencyStore());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddRedisIdempotencyStore_WithoutConfigure_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddRedisIdempotencyStore());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

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
            config.AddRedisIdempotencyStore(options => options.TableName = "CustomIdempotencyKeys")
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomIdempotencyKeys");
        }
    }

    [Test]
    public async Task AddRedisIdempotencyStore_CalledTwice_ReplacesIdempotencyKeyRepository()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
        {
            _ = config.AddRedisIdempotencyStore();
            _ = config.AddRedisIdempotencyStore();
        });

        var repositoryDescriptors = services.Where(d => d.ServiceType == typeof(IIdempotencyKeyRepository)).ToList();
        var storeDescriptors = services.Where(d => d.ServiceType == typeof(IIdempotencyStore)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(repositoryDescriptors.Count).IsEqualTo(1);
            _ = await Assert.That(storeDescriptors.Count).IsEqualTo(1);
            _ = await Assert.That(storeDescriptors[0].ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }
}
