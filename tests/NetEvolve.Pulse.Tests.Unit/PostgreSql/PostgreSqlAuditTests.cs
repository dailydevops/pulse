namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("PostgreSql")]
public sealed class PostgreSqlAuditTests
{
    private const string ValidConnectionString = "Host=localhost;Database=Test;Username=postgres;Password=secret;";

    [Test]
    public async Task Store_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new PostgreSqlAuditStore(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new PostgreSqlAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = null })))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = string.Empty }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new PostgreSqlAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = "   " })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new AuditStoreOptions { ConnectionString = ValidConnectionString };

        var store = new PostgreSqlAuditStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithCustomSchemaAndTableName_CreatesInstance()
    {
        var options = new AuditStoreOptions
        {
            ConnectionString = ValidConnectionString,
            Schema = "custom",
            TableName = "CustomAuditEntry",
        };

        var store = new PostgreSqlAuditStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithNullSchema_CreatesInstance()
    {
        var options = new AuditStoreOptions { ConnectionString = ValidConnectionString, Schema = null };

        var store = new PostgreSqlAuditStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Management_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new PostgreSqlAuditManagement(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = null }))
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = string.Empty }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = "   " }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithValidArguments_CreatesInstance()
    {
        var options = Options.Create(new AuditStoreOptions { ConnectionString = ValidConnectionString });

        var management = new PostgreSqlAuditManagement(options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task AddPostgreSqlAuditStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlAuditMediatorBuilderExtensions.AddPostgreSqlAuditStore(
                    null!,
                    opts => opts.ConnectionString = ValidConnectionString
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlAuditStore_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert.That(() => mock.Object.AddPostgreSqlAuditStore(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddPostgreSqlAuditStore_WithValidOptions_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddPostgreSqlAuditStore(opts => opts.ConnectionString = ValidConnectionString);

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddPostgreSqlAuditStore_RegistersAuditStoreAsScoped()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        var services = new ServiceCollection();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddPostgreSqlAuditStore(opts => opts.ConnectionString = ValidConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(PostgreSqlAuditStore));
        }
    }

    [Test]
    public async Task AddPostgreSqlAuditStore_RegistersAuditManagementAsScoped()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        var services = new ServiceCollection();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddPostgreSqlAuditStore(opts => opts.ConnectionString = ValidConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(PostgreSqlAuditManagement));
        }
    }

    [Test]
    public async Task AddPostgreSqlAuditStore_AppliesOptions()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        var services = new ServiceCollection();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddPostgreSqlAuditStore(opts =>
        {
            opts.ConnectionString = ValidConnectionString;
            opts.Schema = "custom";
            opts.TableName = "CustomAuditEntry";
        });

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<AuditStoreOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo(ValidConnectionString);
                _ = await Assert.That(options.Value.Schema).IsEqualTo("custom");
                _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomAuditEntry");
            }
        }
    }
}
