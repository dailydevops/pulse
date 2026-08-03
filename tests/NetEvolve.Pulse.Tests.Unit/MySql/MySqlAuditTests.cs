namespace NetEvolve.Pulse.Tests.Unit.MySql;

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("MySql")]
public sealed class MySqlAuditTests
{
    private const string ValidConnectionString = "Server=localhost;Database=Test;Uid=root;Pwd=secret;";

    [Test]
    public async Task Store_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new MySqlAuditStore(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new MySqlAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = null })))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new MySqlAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = string.Empty })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new MySqlAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = "   " })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var store = new MySqlAuditStore(
            Options.Create(new AuditStoreOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithCustomTableName_CreatesInstance()
    {
        var options = new AuditStoreOptions
        {
            ConnectionString = ValidConnectionString,
            TableName = "CustomAuditEntry",
        };

        var store = new MySqlAuditStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Management_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new MySqlAuditManagement(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new MySqlAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = null })))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MySqlAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = string.Empty }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new MySqlAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = "   " })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithValidArguments_CreatesInstance()
    {
        var management = new MySqlAuditManagement(
            Options.Create(new AuditStoreOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task AddMySqlAuditStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                MySqlAuditMediatorBuilderExtensions.AddMySqlAuditStore(
                    null!,
                    opts => opts.ConnectionString = ValidConnectionString
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlAuditStore_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert.That(() => mock.Object.AddMySqlAuditStore(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddMySqlAuditStore_WithValidOptions_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMySqlAuditStore(opts => opts.ConnectionString = ValidConnectionString);

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMySqlAuditStore_RegistersAuditStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlAuditStore(opts => opts.ConnectionString = ValidConnectionString)
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(MySqlAuditStore));
        }
    }

    [Test]
    public async Task AddMySqlAuditStore_RegistersAuditManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlAuditStore(opts => opts.ConnectionString = ValidConnectionString)
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(MySqlAuditManagement));
        }
    }

    [Test]
    public async Task AddMySqlAuditStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlAuditStore(opts =>
            {
                opts.ConnectionString = ValidConnectionString;
                opts.TableName = "CustomAuditEntry";
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<AuditStoreOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo(ValidConnectionString);
                _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomAuditEntry");
            }
        }
    }
}
