namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

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

[TestGroup("PostgreSql")]
public sealed class PostgreSqlIdempotencyMediatorBuilderExtensionsTests
{
    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlIdempotencyMediatorBuilderExtensions.AddPostgreSqlIdempotencyStore(
                    null!,
                    "Host=localhost;Encrypt=true;"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddPostgreSqlIdempotencyStore((string)null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddPostgreSqlIdempotencyStore(string.Empty))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => Mock.Of<IMediatorBuilder>().Object.AddPostgreSqlIdempotencyStore("   "))
            .Throws<ArgumentException>();

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddPostgreSqlIdempotencyStore("Host=localhost;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithValidConnectionString_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddPostgreSqlIdempotencyStore("Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(PostgreSqlIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithValidConnectionString_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddPostgreSqlIdempotencyStore("Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithValidConnectionString_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddPostgreSqlIdempotencyStore("Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddPostgreSqlIdempotencyStore("Host=localhost;Encrypt=true;", options => options.Schema = "myschema")
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            _ = await Assert.That(options.Value.Schema).IsEqualTo("myschema");
        }
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithFactory_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlIdempotencyMediatorBuilderExtensions.AddPostgreSqlIdempotencyStore(
                    null!,
                    _ => "Host=localhost;Encrypt=true;"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithFactory_WithNullFactory_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                Mock.Of<IMediatorBuilder>().Object.AddPostgreSqlIdempotencyStore((Func<IServiceProvider, string>)null!)
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithFactory_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddPostgreSqlIdempotencyStore(_ => "Host=localhost;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithFactory_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddPostgreSqlIdempotencyStore(_ => "Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(PostgreSqlIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithFactory_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddPostgreSqlIdempotencyStore(_ => "Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(IdempotencyStore));
        }
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithFactory_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddPostgreSqlIdempotencyStore(
                _ => "Host=localhost;Encrypt=true;",
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
    public async Task AddPostgreSqlIdempotencyStore_WithAction_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlIdempotencyMediatorBuilderExtensions.AddPostgreSqlIdempotencyStore(
                    null!,
                    opts => opts.ConnectionString = "Host=localhost;Encrypt=true;"
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithAction_WithNullAction_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert
            .That(() => mock.Object.AddPostgreSqlIdempotencyStore((Action<IdempotencyKeyOptions>)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithAction_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddPostgreSqlIdempotencyStore(opts =>
            opts.ConnectionString = "Host=localhost;Encrypt=true;"
        );

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithAction_RegistersIdempotencyKeyRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddPostgreSqlIdempotencyStore(opts => opts.ConnectionString = "Host=localhost;Encrypt=true;")
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyKeyRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(PostgreSqlIdempotencyKeyRepository));
        }
    }

    [Test]
    public async Task AddPostgreSqlIdempotencyStore_WithAction_RegistersIdempotencyStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddPostgreSqlIdempotencyStore(opts => opts.ConnectionString = "Host=localhost;Encrypt=true;")
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
    public async Task AddPostgreSqlIdempotencyStore_WithAction_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddPostgreSqlIdempotencyStore(opts =>
            {
                opts.ConnectionString = "Host=localhost;Encrypt=true;";
                opts.Schema = "custom";
                opts.TimeToLive = TimeSpan.FromHours(24);
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<IdempotencyKeyOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo("Host=localhost;Encrypt=true;");
                _ = await Assert.That(options.Value.Schema).IsEqualTo("custom");
                _ = await Assert.That(options.Value.TimeToLive).IsEqualTo(TimeSpan.FromHours(24));
            }
        }
    }
}
