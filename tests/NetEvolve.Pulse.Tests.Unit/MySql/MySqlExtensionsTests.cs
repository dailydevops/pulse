namespace NetEvolve.Pulse.Tests.Unit.MySql;

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

[TestGroup("MySql")]
public sealed class MySqlExtensionsTests
{
    [Test]
    public async Task AddMySqlOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => MySqlExtensions.AddMySqlOutbox(null!, "Server=localhost;Database=mydb;"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlOutbox_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddMySqlOutbox((string)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlOutbox_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddMySqlOutbox(string.Empty))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddMySqlOutbox_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddMySqlOutbox("   "))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddMySqlOutbox_WithNullConfigureOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddMySqlOutbox((Action<OutboxOptions>)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlOutbox_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMySqlOutbox("Server=localhost;Database=mydb;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMySqlOutbox_WithValidConnectionString_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlOutbox("Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(OutboxEventStore));
        }
    }

    [Test]
    public async Task AddMySqlOutbox_WithValidConnectionString_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlOutbox("Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddMySqlOutbox_WithValidConnectionString_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlOutbox("Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddMySqlOutbox_WithValidConnectionString_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlOutbox("Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddMySqlOutbox_WithConfigureOptions_AppliesTableName()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlOutbox("Server=localhost;Database=mydb;", options => options.TableName = "CustomTable")
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomTable");
        }
    }

    [Test]
    public async Task AddMySqlOutbox_WithConfigureAction_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlOutbox(opts =>
            {
                opts.ConnectionString = "Server=localhost;Database=mydb;";
                opts.TableName = "Events";
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo("Server=localhost;Database=mydb;");
                _ = await Assert.That(options.Value.TableName).IsEqualTo("Events");
            }
        }
    }

    [Test]
    public async Task AddMySqlOutbox_WithConfigureAction_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMySqlOutbox(opts => opts.ConnectionString = "Server=localhost;Database=mydb;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMySqlOutbox_WithFactory_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                MySqlExtensions.AddMySqlOutbox(null!, (Func<IServiceProvider, string>)(_ => "Server=localhost;"))
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlOutbox_WithFactory_WithNullFactory_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddMySqlOutbox((Func<IServiceProvider, string>)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlOutbox_WithFactory_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMySqlOutbox(_ => "Server=localhost;Database=mydb;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMySqlOutbox_WithFactory_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlOutbox(_ => "Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(OutboxEventStore));
        }
    }

    [Test]
    public async Task AddMySqlOutbox_WithFactory_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlOutbox(_ => "Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddMySqlOutbox_WithFactory_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlOutbox(_ => "Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddMySqlOutbox_WithFactory_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlOutbox(_ => "Server=localhost;Database=mydb;", options => options.TableName = "CustomTable")
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomTable");
        }
    }

    [Test]
    public async Task AddMySqlOutboxTransactionScope_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => MySqlExtensions.AddMySqlOutboxTransactionScope<TestUnitOfWork>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlOutboxTransactionScope_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMySqlOutboxTransactionScope<TestUnitOfWork>();

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMySqlOutboxTransactionScope_RegistersTransactionScopeAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlOutbox("Server=localhost;Database=mydb;").AddMySqlOutboxTransactionScope<TestUnitOfWork>()
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
