namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

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

[TestGroup("SqlServer")]
public sealed class SqlServerExtensionsTests
{
    [Test]
    public async Task AddSqlServerOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => SqlServerExtensions.AddSqlServerOutbox(null!, "Server=.;Encrypt=true;"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerOutbox_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSqlServerOutbox((string)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerOutbox_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSqlServerOutbox(string.Empty))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSqlServerOutbox_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSqlServerOutbox("   "))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSqlServerOutbox_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSqlServerOutbox("Server=.;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
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
    public async Task AddSqlServerOutbox_WithValidConnectionString_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox("Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SqlServerEventOutbox));
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
            .That(() => SqlServerExtensions.AddSqlServerOutbox(null!, _ => "Server=.;Encrypt=true;"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerOutbox_WithFactory_WithNullFactory_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSqlServerOutbox((Func<IServiceProvider, string>)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerOutbox_WithFactory_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSqlServerOutbox(_ => "Server=.;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
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
    public async Task AddSqlServerOutbox_WithFactory_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox(_ => "Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SqlServerEventOutbox));
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

    [Test]
    public async Task AddSqlServerOutbox_WithValidConnectionString_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox("Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddSqlServerOutbox_WithFactory_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddSqlServerOutbox(_ => "Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddSqlServerOutboxTransactionScope_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => SqlServerExtensions.AddSqlServerOutboxTransactionScope<TestUnitOfWork>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerOutboxTransactionScope_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSqlServerOutboxTransactionScope<TestUnitOfWork>();

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSqlServerOutboxTransactionScope_RegistersTransactionScopeAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config
                .AddOutbox()
                .AddSqlServerOutbox("Server=.;Encrypt=true;")
                .AddSqlServerOutboxTransactionScope<TestUnitOfWork>()
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxTransactionScope));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(TestUnitOfWork));
        }
    }

    private sealed class TestUnitOfWork : IOutboxTransactionScope
    {
        public object? GetCurrentTransaction() => null;
    }
}
