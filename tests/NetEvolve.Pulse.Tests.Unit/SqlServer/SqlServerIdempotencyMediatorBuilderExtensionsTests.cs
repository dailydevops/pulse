namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

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

[TestGroup("SqlServer")]
public sealed class SqlServerIdempotencyMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddSqlServerIdempotencyStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SqlServerIdempotencyMediatorBuilderExtensions.AddSqlServerIdempotencyStore(
                    null!,
                    "Server=.;Encrypt=true;"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSqlServerIdempotencyStore((string)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSqlServerIdempotencyStore(string.Empty))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddSqlServerIdempotencyStore("   "))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSqlServerIdempotencyStore("Server=.;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithValidConnectionString_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSqlServerIdempotencyStore("Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SqlServerIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithValidConnectionString_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSqlServerIdempotencyStore("Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithValidConnectionString_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSqlServerIdempotencyStore("Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerIdempotencyStore("Server=.;Encrypt=true;", options => options.Schema = "myschema")
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<SqlServerIdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.Schema).IsEqualTo("myschema");
        }
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithFactory_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SqlServerIdempotencyMediatorBuilderExtensions.AddSqlServerIdempotencyStore(
                    null!,
                    _ => "Server=.;Encrypt=true;"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithFactory_WithNullFactory_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                Mock.Of<IMediatorBuilder>().Object.AddSqlServerIdempotencyStore((Func<IServiceProvider, string>)null!)
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithFactory_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSqlServerIdempotencyStore(_ => "Server=.;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithFactory_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSqlServerIdempotencyStore(_ => "Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SqlServerIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithFactory_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddSqlServerIdempotencyStore(_ => "Server=.;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithFactory_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerIdempotencyStore(
                _ => "Server=.;Encrypt=true;",
                options => options.TableName = "CustomTable"
            )
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<SqlServerIdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomTable");
        }
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithAction_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SqlServerIdempotencyMediatorBuilderExtensions.AddSqlServerIdempotencyStore(
                    null!,
                    opts => opts.ConnectionString = "Server=.;Encrypt=true;"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithAction_WithNullAction_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert
            .That(() => mock.Object.AddSqlServerIdempotencyStore((Action<SqlServerIdempotencyKeyOptions>)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithAction_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSqlServerIdempotencyStore(opts => opts.ConnectionString = "Server=.;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithAction_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerIdempotencyStore(opts => opts.ConnectionString = "Server=.;Encrypt=true;")
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(SqlServerIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddSqlServerIdempotencyStore_WithAction_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerIdempotencyStore(opts => opts.ConnectionString = "Server=.;Encrypt=true;")
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
    public async Task AddSqlServerIdempotencyStore_WithAction_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerIdempotencyStore(opts =>
            {
                opts.ConnectionString = "Server=.;Encrypt=true;";
                opts.Schema = "custom";
                opts.TimeToLive = TimeSpan.FromHours(24);
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<SqlServerIdempotencyKeyOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo("Server=.;Encrypt=true;");
                _ = await Assert.That(options.Value.Schema).IsEqualTo("custom");
                _ = await Assert.That(options.Value.TimeToLive).IsEqualTo(TimeSpan.FromHours(24));
            }
        }
    }
}
