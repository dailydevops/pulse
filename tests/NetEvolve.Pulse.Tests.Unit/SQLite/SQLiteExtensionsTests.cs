namespace NetEvolve.Pulse.Tests.Unit.SQLite;

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

[TestGroup("SQLite")]
public sealed class SQLiteExtensionsTests
{
    [Test]
    public async Task UseSQLiteOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => SQLiteExtensions.UseSQLiteOutbox(null!, "Data Source=:memory:"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseSQLiteOutbox_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.UseSQLiteOutbox((string)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseSQLiteOutbox_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.UseSQLiteOutbox(string.Empty))
            .Throws<ArgumentException>();

    [Test]
    public async Task UseSQLiteOutbox_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.UseSQLiteOutbox("   "))
            .Throws<ArgumentException>();

    [Test]
    public async Task UseSQLiteOutbox_WithNullConfigureOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.UseSQLiteOutbox((Action<OutboxOptions>)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task UseSQLiteOutbox_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.UseSQLiteOutbox("Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
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
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

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
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

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
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.UseSQLiteOutbox(opts => opts.ConnectionString = "Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => SQLiteExtensions.AddSQLiteOutbox(null!, "Data Source=:memory:"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteOutbox_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSQLiteOutbox((string)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteOutbox_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSQLiteOutbox(string.Empty))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSQLiteOutbox_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSQLiteOutbox("   "))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSQLiteOutbox_WithNullConfigureOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSQLiteOutbox((Action<OutboxOptions>)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteOutbox_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSQLiteOutbox("Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteOutbox_WithValidConnectionString_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteOutbox("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SQLiteEventOutbox));
        }
    }

    [Test]
    public async Task AddSQLiteOutbox_WithValidConnectionString_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteOutbox("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddSQLiteOutbox_WithValidConnectionString_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteOutbox("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddSQLiteOutbox_WithValidConnectionString_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteOutbox("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddSQLiteOutbox_WithConfigureOptions_AppliesTableName()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSQLiteOutbox("Data Source=:memory:", options => options.TableName = "CustomTable")
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomTable");
    }

    [Test]
    public async Task AddSQLiteOutbox_WithConfigureAction_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSQLiteOutbox(opts =>
            {
                opts.ConnectionString = "Data Source=:memory:";
                opts.EnableWalMode = false;
                opts.TableName = "Events";
            })
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.Value.ConnectionString).IsEqualTo("Data Source=:memory:");
            _ = await Assert.That(options.Value.EnableWalMode).IsFalse();
            _ = await Assert.That(options.Value.TableName).IsEqualTo("Events");
        }
    }

    [Test]
    public async Task AddSQLiteOutbox_WithConfigureAction_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSQLiteOutbox(opts => opts.ConnectionString = "Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteOutbox_WithFactory_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => SQLiteExtensions.AddSQLiteOutbox(null!, _ => "Data Source=:memory:"))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteOutbox_WithFactory_WithNullFactory_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSQLiteOutbox((Func<IServiceProvider, string>)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteOutbox_WithFactory_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSQLiteOutbox(_ => "Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteOutbox_WithFactory_RegistersEventOutboxAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteOutbox(_ => "Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventOutbox));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SQLiteEventOutbox));
        }
    }

    [Test]
    public async Task AddSQLiteOutbox_WithFactory_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteOutbox(_ => "Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddSQLiteOutbox_WithFactory_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteOutbox(_ => "Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddSQLiteOutbox_WithFactory_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSQLiteOutbox(_ => "Data Source=:memory:", options => options.TableName = "CustomTable")
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomTable");
    }

    [Test]
    public async Task AddSQLiteOutboxTransactionScope_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => SQLiteExtensions.AddSQLiteOutboxTransactionScope<TestUnitOfWork>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteOutboxTransactionScope_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSQLiteOutboxTransactionScope<TestUnitOfWork>();

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteOutboxTransactionScope_RegistersTransactionScopeAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSQLiteOutbox("Data Source=:memory:").AddSQLiteOutboxTransactionScope<TestUnitOfWork>()
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
