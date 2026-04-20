namespace NetEvolve.Pulse.Tests.Unit.MongoDB;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("MongoDB")]
public sealed class MongoDbExtensionsTests
{
    [Test]
    public async Task UseMongoDbOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => MongoDbExtensions.UseMongoDbOutbox(null!, _ => { }))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseMongoDbOutbox_WithNullConfigureOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.UseMongoDbOutbox(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseMongoDbOutbox_WithValidOptions_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.UseMongoDbOutbox(opts => opts.DatabaseName = "testdb");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task UseMongoDbOutbox_WithValidOptions_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().UseMongoDbOutbox(opts => opts.DatabaseName = "testdb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseMongoDbOutbox_WithValidOptions_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseMongoDbOutbox(opts => opts.DatabaseName = "testdb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseMongoDbOutbox_WithConfigureAction_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config
                .AddOutbox()
                .UseMongoDbOutbox(opts =>
                {
                    opts.DatabaseName = "mydb";
                    opts.CollectionName = "my_outbox";
                })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<MongoDbOutboxOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.DatabaseName).IsEqualTo("mydb");
                _ = await Assert.That(options.Value.CollectionName).IsEqualTo("my_outbox");
            }
        }
    }

    [Test]
    public async Task AddMongoDbOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => MongoDbExtensions.AddMongoDbOutbox(null!, _ => { }))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMongoDbOutbox_WithNullConfigureOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddMongoDbOutbox(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMongoDbOutbox_WithValidOptions_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMongoDbOutbox(opts => opts.DatabaseName = "testdb");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMongoDbOutbox_WithValidOptions_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMongoDbOutbox(opts => opts.DatabaseName = "testdb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(OutboxEventStore));
        }
    }

    [Test]
    public async Task AddMongoDbOutbox_WithValidOptions_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMongoDbOutbox(opts => opts.DatabaseName = "testdb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddMongoDbOutbox_WithValidOptions_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMongoDbOutbox(opts => opts.DatabaseName = "testdb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddMongoDbOutbox_WithConfigureAction_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMongoDbOutbox(opts =>
            {
                opts.DatabaseName = "mydb";
                opts.CollectionName = "my_outbox";
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<MongoDbOutboxOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.DatabaseName).IsEqualTo("mydb");
                _ = await Assert.That(options.Value.CollectionName).IsEqualTo("my_outbox");
            }
        }
    }
}
