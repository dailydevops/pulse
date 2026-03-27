namespace NetEvolve.Pulse.SqlServer.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class SqlServerMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task AddSqlServerOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => SqlServerMediatorConfiguratorExtensions.AddSqlServerOutbox(null!, "Server=.;Encrypt=true;"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerOutbox_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SqlServerMediatorConfiguratorExtensions.AddSqlServerOutbox(
                    new MediatorConfiguratorStub(),
                    (string)null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerOutbox_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                SqlServerMediatorConfiguratorExtensions.AddSqlServerOutbox(new MediatorConfiguratorStub(), string.Empty)
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSqlServerOutbox_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                SqlServerMediatorConfiguratorExtensions.AddSqlServerOutbox(new MediatorConfiguratorStub(), "   ")
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSqlServerOutbox_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var stub = new MediatorConfiguratorStub();

        var result = stub.AddSqlServerOutbox("Server=.;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(stub);
    }

    [Test]
    public async Task AddSqlServerOutbox_WithValidConnectionString_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox("Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddSqlServerOutbox_WithValidConnectionString_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSqlServerOutbox("Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddSqlServerOutbox_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddOutbox().AddSqlServerOutbox("Server=.;Encrypt=true;", options => options.Schema = "myschema")
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        _ = await Assert.That(options.Value.Schema).IsEqualTo("myschema");
    }

    [Test]
    public async Task AddSqlServerOutbox_WithFactory_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SqlServerMediatorConfiguratorExtensions.AddSqlServerOutbox(
                    null!,
                    (Func<IServiceProvider, string>)(_ => "Server=.;Encrypt=true;")
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerOutbox_WithFactory_WithNullFactory_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SqlServerMediatorConfiguratorExtensions.AddSqlServerOutbox(
                    new MediatorConfiguratorStub(),
                    (Func<IServiceProvider, string>)null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerOutbox_WithFactory_ReturnsConfiguratorForChaining()
    {
        var stub = new MediatorConfiguratorStub();

        var result = stub.AddSqlServerOutbox(_ => "Server=.;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(stub);
    }

    [Test]
    public async Task AddSqlServerOutbox_WithFactory_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox(_ => "Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddSqlServerOutbox_WithFactory_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config
                .AddOutbox()
                .AddSqlServerOutbox(_ => "Server=.;Encrypt=true;", options => options.TableName = "CustomTable")
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomTable");
    }

    private sealed class MediatorConfiguratorStub : IMediatorConfigurator
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public IMediatorConfigurator AddActivityAndMetrics() => throw new NotImplementedException();

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
