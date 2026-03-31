namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

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
using NetEvolve.Pulse.Testing;
using TUnit.Core;

public sealed class PostgreSqlMediatorConfiguratorExtensionsTests
{
    [Test]
    public async Task AddPostgreSqlOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlMediatorConfiguratorExtensions.AddPostgreSqlOutbox(null!, "Host=localhost;Encrypt=true;")
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlOutbox_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlMediatorConfiguratorExtensions.AddPostgreSqlOutbox(
                    new MediatorConfiguratorStub(),
                    (string)null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlOutbox_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlMediatorConfiguratorExtensions.AddPostgreSqlOutbox(
                    new MediatorConfiguratorStub(),
                    string.Empty
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task AddPostgreSqlOutbox_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlMediatorConfiguratorExtensions.AddPostgreSqlOutbox(new MediatorConfiguratorStub(), "   ")
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task AddPostgreSqlOutbox_WithValidConnectionString_ReturnsConfiguratorForChaining()
    {
        var stub = new MediatorConfiguratorStub();

        var result = stub.AddPostgreSqlOutbox("Host=localhost;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(stub);
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithValidConnectionString_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddPostgreSqlOutbox("Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithValidConnectionString_RegistersTimeProviderAsSingleton()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddPostgreSqlOutbox("Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TimeProvider));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        }
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config
                .AddOutbox()
                .AddPostgreSqlOutbox("Host=localhost;Encrypt=true;", options => options.Schema = "myschema")
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        _ = await Assert.That(options.Value.Schema).IsEqualTo("myschema");
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithFactory_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlMediatorConfiguratorExtensions.AddPostgreSqlOutbox(
                    null!,
                    (Func<IServiceProvider, string>)(_ => "Host=localhost;Encrypt=true;")
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlOutbox_WithFactory_WithNullFactory_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlMediatorConfiguratorExtensions.AddPostgreSqlOutbox(
                    new MediatorConfiguratorStub(),
                    (Func<IServiceProvider, string>)null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlOutbox_WithFactory_ReturnsConfiguratorForChaining()
    {
        var stub = new MediatorConfiguratorStub();

        var result = stub.AddPostgreSqlOutbox(_ => "Host=localhost;Encrypt=true;");

        _ = await Assert.That(result).IsSameReferenceAs(stub);
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithFactory_RegistersOutboxRepositoryAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddPostgreSqlOutbox(_ => "Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxRepository));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithFactory_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config
                .AddOutbox()
                .AddPostgreSqlOutbox(_ => "Host=localhost;Encrypt=true;", options => options.TableName = "CustomTable")
        );

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomTable");
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithValidConnectionString_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddPostgreSqlOutbox("Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    public async Task AddPostgreSqlOutbox_WithFactory_RegistersOutboxManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config => config.AddOutbox().AddPostgreSqlOutbox(_ => "Host=localhost;Encrypt=true;"));

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }
}
