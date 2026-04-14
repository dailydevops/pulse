namespace NetEvolve.Pulse.Tests.Unit.SQLite;

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

[TestGroup("SQLite")]
public sealed class SQLiteIdempotencyMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddSQLiteIdempotencyStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SQLiteIdempotencyMediatorBuilderExtensions.AddSQLiteIdempotencyStore(null!, "Data Source=:memory:")
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSQLiteIdempotencyStore((string)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSQLiteIdempotencyStore(string.Empty))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSQLiteIdempotencyStore("   "))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSQLiteIdempotencyStore("Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithValidConnectionString_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteIdempotencyStore("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SQLiteIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithValidConnectionString_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteIdempotencyStore("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithValidConnectionString_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteIdempotencyStore("Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSQLiteIdempotencyStore(
                "Data Source=:memory:",
                options => options.TableName = "CustomIdempotencyKeys"
            )
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomIdempotencyKeys");
        }
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithFactory_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SQLiteIdempotencyMediatorBuilderExtensions.AddSQLiteIdempotencyStore(null!, _ => "Data Source=:memory:")
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithFactory_WithNullFactory_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                Mock.Of<IMediatorBuilder>().Object.AddSQLiteIdempotencyStore((Func<IServiceProvider, string>)null!)
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithFactory_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSQLiteIdempotencyStore(_ => "Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithFactory_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteIdempotencyStore(_ => "Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SQLiteIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithFactory_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSQLiteIdempotencyStore(_ => "Data Source=:memory:"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithFactory_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSQLiteIdempotencyStore(
                _ => "Data Source=:memory:",
                options => options.TableName = "CustomIdempotencyKeys"
            )
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomIdempotencyKeys");
        }
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithAction_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SQLiteIdempotencyMediatorBuilderExtensions.AddSQLiteIdempotencyStore(
                    null!,
                    opts => opts.ConnectionString = "Data Source=:memory:"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithAction_WithNullAction_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert
            .That(() => mock.Object.AddSQLiteIdempotencyStore((Action<IdempotencyKeyOptions>)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithAction_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSQLiteIdempotencyStore(opts => opts.ConnectionString = "Data Source=:memory:");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithAction_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSQLiteIdempotencyStore(opts => opts.ConnectionString = "Data Source=:memory:")
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SQLiteIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithAction_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSQLiteIdempotencyStore(opts => opts.ConnectionString = "Data Source=:memory:")
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddSQLiteIdempotencyStore_WithAction_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSQLiteIdempotencyStore(opts =>
            {
                opts.ConnectionString = "Data Source=:memory:";
                opts.TableName = "CustomIdempotencyKeys";
                opts.EnableWalMode = false;
                opts.TimeToLive = TimeSpan.FromHours(24);
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo("Data Source=:memory:");
                _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomIdempotencyKeys");
                _ = await Assert.That(options.Value.EnableWalMode).IsFalse();
                _ = await Assert.That(options.Value.TimeToLive).IsEqualTo(TimeSpan.FromHours(24));
            }
        }
    }
}
