namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("SQLite")]
public sealed class SQLiteAuditTests
{
    private const string ValidConnectionString = "Data Source=:memory:";

    [Test]
    public async Task Store_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new SQLiteAuditStore(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SQLiteAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = null })))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new SQLiteAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = string.Empty })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new SQLiteAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = "   " })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new AuditStoreOptions { ConnectionString = ValidConnectionString };

        var store = new SQLiteAuditStore(Options.Create(options));

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

        var store = new SQLiteAuditStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithWalModeDisabled_CreatesInstance()
    {
        var options = new AuditStoreOptions { ConnectionString = ValidConnectionString, EnableWalMode = false };

        var store = new SQLiteAuditStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithInvalidTableName_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteAuditStore(
                    Options.Create(
                        new AuditStoreOptions { ConnectionString = ValidConnectionString, TableName = "1invalid" }
                    )
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new SQLiteAuditManagement(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SQLiteAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = null })))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = string.Empty }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new SQLiteAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = "   " })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new AuditStoreOptions { ConnectionString = ValidConnectionString };

        var management = new SQLiteAuditManagement(Options.Create(options));

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Management_Constructor_WithInvalidTableName_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteAuditManagement(
                    Options.Create(
                        new AuditStoreOptions { ConnectionString = ValidConnectionString, TableName = "1invalid" }
                    )
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSQLiteAuditStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SQLiteAuditMediatorBuilderExtensions.AddSQLiteAuditStore(
                    null!,
                    opts => opts.ConnectionString = ValidConnectionString
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteAuditStore_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert.That(() => mock.Object.AddSQLiteAuditStore(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddSQLiteAuditStore_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSQLiteAuditStore(opts => opts.ConnectionString = ValidConnectionString);

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteAuditStore_RegistersAuditStoreAsScoped()
    {
        var services = new ServiceCollection();
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddSQLiteAuditStore(opts => opts.ConnectionString = ValidConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(SQLiteAuditStore));
        }
    }

    [Test]
    public async Task AddSQLiteAuditStore_RegistersAuditManagementAsScoped()
    {
        var services = new ServiceCollection();
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddSQLiteAuditStore(opts => opts.ConnectionString = ValidConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(SQLiteAuditManagement));
        }
    }

    [Test]
    public async Task AddSQLiteAuditStore_AppliesOptions()
    {
        var services = new ServiceCollection();
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddSQLiteAuditStore(opts =>
        {
            opts.ConnectionString = ValidConnectionString;
            opts.TableName = "CustomAuditEntry";
            opts.EnableWalMode = false;
        });

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<AuditStoreOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo(ValidConnectionString);
                _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomAuditEntry");
                _ = await Assert.That(options.Value.EnableWalMode).IsFalse();
            }
        }
    }
}
