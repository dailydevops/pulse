namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

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

[TestGroup("SqlServer")]
public sealed class SqlServerAuditTests
{
    private const string ValidConnectionString = "Server=.;Database=Test;Integrated Security=true;";

    [Test]
    public async Task Store_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new SqlServerAuditStore(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SqlServerAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = null })))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = string.Empty }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new SqlServerAuditStore(Options.Create(new AuditStoreOptions { ConnectionString = "   " })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var store = new SqlServerAuditStore(
            Options.Create(new AuditStoreOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithCustomTableName_CreatesInstance()
    {
        var options = new AuditStoreOptions { ConnectionString = ValidConnectionString, TableName = "CustomAudit" };

        var store = new SqlServerAuditStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    // Defense-in-depth: pin that an attacker-controlled Schema value cannot reach the SQL
    // builder. The constructor must fail fast when Schema contains characters that would
    // break out of the [bracketed] identifier (e.g. ']' followed by injected SQL).
    [Test]
    public async Task Store_Constructor_WithMaliciousSchema_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerAuditStore(
                    Options.Create(
                        new AuditStoreOptions { ConnectionString = ValidConnectionString, Schema = "pulse].[evil] -- " }
                    )
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new SqlServerAuditManagement(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SqlServerAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = null })))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = string.Empty }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerAuditManagement(Options.Create(new AuditStoreOptions { ConnectionString = "   " }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithValidArguments_CreatesInstance()
    {
        var management = new SqlServerAuditManagement(
            Options.Create(new AuditStoreOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert.That(management).IsNotNull();
    }

    // Defense-in-depth: pin that an attacker-controlled Schema value cannot reach the SQL
    // builder. The constructor must fail fast when Schema contains characters that would
    // break out of the [bracketed] identifier (e.g. ']' followed by injected SQL).
    [Test]
    public async Task Management_Constructor_WithMaliciousSchema_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerAuditManagement(
                    Options.Create(
                        new AuditStoreOptions { ConnectionString = ValidConnectionString, Schema = "pulse].[evil] -- " }
                    )
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSqlServerAuditStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SqlServerAuditMediatorBuilderExtensions.AddSqlServerAuditStore(
                    null!,
                    opts => opts.ConnectionString = ValidConnectionString
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerAuditStore_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert.That(() => mock.Object.AddSqlServerAuditStore(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddSqlServerAuditStore_WithValidOptions_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSqlServerAuditStore(opts => opts.ConnectionString = ValidConnectionString);

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSqlServerAuditStore_RegistersAuditStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerAuditStore(opts => opts.ConnectionString = ValidConnectionString)
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(SqlServerAuditStore));
        }
    }

    [Test]
    public async Task AddSqlServerAuditStore_RegistersAuditManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerAuditStore(opts => opts.ConnectionString = ValidConnectionString)
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(SqlServerAuditManagement));
        }
    }

    [Test]
    public async Task AddSqlServerAuditStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerAuditStore(opts =>
            {
                opts.ConnectionString = ValidConnectionString;
                opts.TableName = "CustomAudit";
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<AuditStoreOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo(ValidConnectionString);
                _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomAudit");
            }
        }
    }
}
