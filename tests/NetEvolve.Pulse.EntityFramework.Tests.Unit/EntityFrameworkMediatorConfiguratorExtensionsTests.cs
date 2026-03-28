namespace NetEvolve.Pulse.EntityFramework.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class EntityFrameworkMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task AddEntityFrameworkOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => EntityFrameworkMediatorConfiguratorExtensions.AddEntityFrameworkOutbox<TestDbContext>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddEntityFrameworkOutbox_WithValidConfigurator_ReturnsConfiguratorForChaining()
    {
        var stub = new MediatorConfiguratorStub();

        var result = stub.AddEntityFrameworkOutbox<TestDbContext>();

        _ = await Assert.That(result).IsSameReferenceAs(stub);
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkOutbox_RegistersOutboxRepositoryAsScoped))
        );
        _ = services.AddPulse(config => config.AddOutbox().AddEntityFrameworkOutbox<TestDbContext>());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkOutbox_RegistersEventOutboxAsScoped))
        );
        _ = services.AddPulse(config => config.AddOutbox().AddEntityFrameworkOutbox<TestDbContext>());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_RegistersTransactionScopeAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkOutbox_RegistersTransactionScopeAsScoped))
        );
        _ = services.AddPulse(config => config.AddOutbox().AddEntityFrameworkOutbox<TestDbContext>());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxTransactionScope));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddEntityFrameworkOutbox<TestDbContext>());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkOutbox_WithConfigureOptions_AppliesOptions))
        );
        _ = services.AddPulse(config =>
            config.AddOutbox().AddEntityFrameworkOutbox<TestDbContext>(options => options.Schema = "myschema")
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        _ = await Assert.That(options.Value.Schema).IsEqualTo("myschema");
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_WithTableNameOption_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkOutbox_WithTableNameOption_AppliesOptions))
        );
        _ = services.AddPulse(config =>
            config.AddOutbox().AddEntityFrameworkOutbox<TestDbContext>(options => options.TableName = "CustomOutbox")
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomOutbox");
    }

    [Test]
    public async Task AddEntityFrameworkOutbox_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddDbContext<TestDbContext>(o =>
            o.UseInMemoryDatabase(nameof(AddEntityFrameworkOutbox_RegistersOutboxManagementAsScoped))
        );
        _ = services.AddPulse(config => config.AddOutbox().AddEntityFrameworkOutbox<TestDbContext>());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    private sealed class MediatorConfiguratorStub : IMediatorConfigurator
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public IMediatorConfigurator AddActivityAndMetrics() => throw new NotImplementedException();

        public IMediatorConfigurator AddQueryCaching(Action<QueryCachingOptions>? configure = null) =>
            throw new NotImplementedException();

        public IMediatorConfigurator UseDefaultEventDispatcher<TDispatcher>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();

        public IMediatorConfigurator UseDefaultEventDispatcher<TDispatcher>(
            Func<IServiceProvider, TDispatcher> factory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();

        public IMediatorConfigurator UseEventDispatcherFor<TEvent, TDispatcher>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TEvent : IEvent
            where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();

        public IMediatorConfigurator UseEventDispatcherFor<TEvent, TDispatcher>(
            Func<IServiceProvider, TDispatcher> factory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TEvent : IEvent
            where TDispatcher : class, IEventDispatcher => throw new NotImplementedException();
    }
}
