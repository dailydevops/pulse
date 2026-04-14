namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkIdempotencyExtensionsTests
{
    [Test]
    public async Task AddEntityFrameworkIdempotencyStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                EntityFrameworkIdempotencyExtensions.AddEntityFrameworkIdempotencyStore<TestIdempotencyDbContext>(null!)
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddEntityFrameworkIdempotencyStore_WithValidConfigurator_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddEntityFrameworkIdempotencyStore<TestIdempotencyDbContext>();

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddEntityFrameworkIdempotencyStore_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestIdempotencyDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkIdempotencyStore_RegistersIdempotencyStoreAsScoped))
        );
        _ = services.AddPulse(config =>
            config.AddIdempotency().AddEntityFrameworkIdempotencyStore<TestIdempotencyDbContext>()
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddEntityFrameworkIdempotencyStore_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddEntityFrameworkIdempotencyStore<TestIdempotencyDbContext>());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddEntityFrameworkIdempotencyStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestIdempotencyDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkIdempotencyStore_WithConfigureOptions_AppliesOptions))
        );
        _ = services.AddPulse(config =>
            config
                .AddIdempotency()
                .AddEntityFrameworkIdempotencyStore<TestIdempotencyDbContext>(options => options.Schema = "myschema")
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.Schema).IsEqualTo("myschema");
        }
    }

    [Test]
    public async Task AddEntityFrameworkIdempotencyStore_WithTableNameOption_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestIdempotencyDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkIdempotencyStore_WithTableNameOption_AppliesOptions))
        );
        _ = services.AddPulse(config =>
            config
                .AddIdempotency()
                .AddEntityFrameworkIdempotencyStore<TestIdempotencyDbContext>(options =>
                    options.TableName = "CustomIdempotency"
                )
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomIdempotency");
        }
    }

    [Test]
    public async Task AddEntityFrameworkIdempotencyStore_WithTimeToLiveOption_AppliesOptions()
    {
        var ttl = TimeSpan.FromMinutes(30);
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestIdempotencyDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkIdempotencyStore_WithTimeToLiveOption_AppliesOptions))
        );
        _ = services.AddPulse(config =>
            config
                .AddIdempotency()
                .AddEntityFrameworkIdempotencyStore<TestIdempotencyDbContext>(options => options.TimeToLive = ttl)
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.TimeToLive).IsEqualTo(ttl);
        }
    }
}
