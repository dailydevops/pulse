namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("CosmosDb")]
public sealed class CosmosDbExtensionsTests
{
    [Test]
    public async Task AddCosmosDbOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => CosmosDbExtensions.AddCosmosDbOutbox(null!, _ => { }))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddCosmosDbOutbox_WithNullConfigureOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddCosmosDbOutbox(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddCosmosDbOutbox_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddCosmosDbOutbox(opts => opts.DatabaseName = "TestDb");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddCosmosDbOutbox_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddCosmosDbOutbox(opts => opts.DatabaseName = "TestDb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddCosmosDbOutbox_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddCosmosDbOutbox(opts => opts.DatabaseName = "TestDb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(OutboxEventStore));
        }
    }

    [Test]
    public async Task AddCosmosDbOutbox_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddCosmosDbOutbox(opts => opts.DatabaseName = "TestDb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddCosmosDbOutbox_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddCosmosDbOutbox(opts => opts.DatabaseName = "TestDb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddCosmosDbOutbox_AppliesConfiguredOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddCosmosDbOutbox(opts =>
            {
                opts.DatabaseName = "MyDatabase";
                opts.ContainerName = "my_outbox";
                opts.EnableTimeToLive = true;
                opts.TtlSeconds = 3600;
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<CosmosDbOutboxOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.DatabaseName).IsEqualTo("MyDatabase");
                _ = await Assert.That(options.Value.ContainerName).IsEqualTo("my_outbox");
                _ = await Assert.That(options.Value.EnableTimeToLive).IsTrue();
                _ = await Assert.That(options.Value.TtlSeconds).IsEqualTo(3600);
            }
        }
    }

    [Test]
    public async Task UseCosmosDbOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => CosmosDbExtensions.UseCosmosDbOutbox(null!, _ => { }))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseCosmosDbOutbox_WithNullConfigureOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.UseCosmosDbOutbox(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseCosmosDbOutbox_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.UseCosmosDbOutbox(opts => opts.DatabaseName = "TestDb");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task UseCosmosDbOutbox_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().UseCosmosDbOutbox(opts => opts.DatabaseName = "TestDb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseCosmosDbOutbox_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().UseCosmosDbOutbox(opts => opts.DatabaseName = "TestDb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseCosmosDbOutbox_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().UseCosmosDbOutbox(opts => opts.DatabaseName = "TestDb"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }
}
