namespace NetEvolve.Pulse.Tests.Unit.MySql;

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

[TestGroup("MySql")]
public sealed class MySqlIdempotencyMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddMySqlIdempotencyStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                MySqlIdempotencyMediatorBuilderExtensions.AddMySqlIdempotencyStore(
                    null!,
                    "Server=localhost;Database=mydb;"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlIdempotencyStore_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddMySqlIdempotencyStore((string)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlIdempotencyStore_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddMySqlIdempotencyStore(string.Empty))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddMySqlIdempotencyStore_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddMySqlIdempotencyStore("   "))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddMySqlIdempotencyStore_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMySqlIdempotencyStore("Server=localhost;Database=mydb;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithValidConnectionString_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlIdempotencyStore("Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(MySqlIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithValidConnectionString_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlIdempotencyStore("Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithValidConnectionString_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlIdempotencyStore("Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlIdempotencyStore(
                "Server=localhost;Database=mydb;",
                options => options.TableName = "CustomIdempotencyKey"
            )
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomIdempotencyKey");
        }
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithFactory_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                MySqlIdempotencyMediatorBuilderExtensions.AddMySqlIdempotencyStore(
                    null!,
                    _ => "Server=localhost;Database=mydb;"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlIdempotencyStore_WithFactory_WithNullFactory_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                Mock.Of<IMediatorBuilder>().Object.AddMySqlIdempotencyStore((Func<IServiceProvider, string>)null!)
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlIdempotencyStore_WithFactory_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMySqlIdempotencyStore(_ => "Server=localhost;Database=mydb;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithFactory_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlIdempotencyStore(_ => "Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(MySqlIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithFactory_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddMySqlIdempotencyStore(_ => "Server=localhost;Database=mydb;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithFactory_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlIdempotencyStore(
                _ => "Server=localhost;Database=mydb;",
                options => options.TableName = "CustomTable"
            )
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomTable");
        }
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithAction_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                MySqlIdempotencyMediatorBuilderExtensions.AddMySqlIdempotencyStore(
                    null!,
                    opts => opts.ConnectionString = "Server=localhost;Database=mydb;"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlIdempotencyStore_WithAction_WithNullAction_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert
            .That(() => mock.Object.AddMySqlIdempotencyStore((Action<IdempotencyKeyOptions>)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithAction_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMySqlIdempotencyStore(opts =>
            opts.ConnectionString = "Server=localhost;Database=mydb;"
        );

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithAction_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlIdempotencyStore(opts => opts.ConnectionString = "Server=localhost;Database=mydb;")
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(MySqlIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddMySqlIdempotencyStore_WithAction_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlIdempotencyStore(opts => opts.ConnectionString = "Server=localhost;Database=mydb;")
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
    public async Task AddMySqlIdempotencyStore_WithAction_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlIdempotencyStore(opts =>
            {
                opts.ConnectionString = "Server=localhost;Database=mydb;";
                opts.TableName = "CustomIdempotencyKey";
                opts.TimeToLive = TimeSpan.FromHours(24);
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo("Server=localhost;Database=mydb;");
                _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomIdempotencyKey");
                _ = await Assert.That(options.Value.TimeToLive).IsEqualTo(TimeSpan.FromHours(24));
            }
        }
    }
}
