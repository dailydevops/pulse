namespace NetEvolve.Pulse.SQLite.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Caching;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class SQLiteMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task UseSQLiteOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => SQLiteMediatorConfiguratorExtensions.UseSQLiteOutbox(null!, "Data Source=:memory:"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseSQLiteOutbox_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SQLiteMediatorConfiguratorExtensions.UseSQLiteOutbox(new MediatorConfiguratorStub(), (string)null!)
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseSQLiteOutbox_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                SQLiteMediatorConfiguratorExtensions.UseSQLiteOutbox(new MediatorConfiguratorStub(), string.Empty)
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task UseSQLiteOutbox_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => SQLiteMediatorConfiguratorExtensions.UseSQLiteOutbox(new MediatorConfiguratorStub(), "   "))
            .Throws<ArgumentException>();

    [Test]
    public async Task UseSQLiteOutbox_WithNullConfigureOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SQLiteMediatorConfiguratorExtensions.UseSQLiteOutbox(
                    new MediatorConfiguratorStub(),
                    (Action<SQLiteOutboxOptions>)null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseSQLiteOutbox_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var stub = new MediatorConfiguratorStub();

        var result = stub.UseSQLiteOutbox("Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(stub);
    }

    [Test]
    public async Task UseSQLiteOutbox_WithValidConnectionString_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().UseSQLiteOutbox("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseSQLiteOutbox_WithValidConnectionString_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().UseSQLiteOutbox("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task UseSQLiteOutbox_WithValidConnectionString_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseSQLiteOutbox("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task UseSQLiteOutbox_WithConfigureOptions_AppliesTableName()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddOutbox().UseSQLiteOutbox("Data Source=:memory:", options => options.TableName = "CustomTable")
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SQLiteOutboxOptions>>();

        _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomTable");
    }

    [Test]
    public async Task UseSQLiteOutbox_WithConfigureAction_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config
                .AddOutbox()
                .UseSQLiteOutbox(opts =>
                {
                    opts.ConnectionString = "Data Source=:memory:";
                    opts.EnableWalMode = false;
                    opts.TableName = "Events";
                })
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SQLiteOutboxOptions>>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.Value.ConnectionString).IsEqualTo("Data Source=:memory:");
            _ = await Assert.That(options.Value.EnableWalMode).IsFalse();
            _ = await Assert.That(options.Value.TableName).IsEqualTo("Events");
        }
    }

    [Test]
    public async Task UseSQLiteOutbox_WithConfigureAction_ReturnsConfiguratorForChaining()
    {
        var stub = new MediatorConfiguratorStub();

        var result = stub.UseSQLiteOutbox(opts => opts.ConnectionString = "Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(stub);
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
