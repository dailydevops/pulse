namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;
using TUnit.Mocks;

public sealed class SQLiteMediatorBuilderExtensionsTests
{
    [Test]
    public async Task UseSQLiteOutbox_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => SQLiteMediatorBuilderExtensions.UseSQLiteOutbox(null!, "Data Source=:memory:"))
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
            .That(() => Mock.Of<IMediatorBuilder>().Object.UseSQLiteOutbox((Action<SQLiteOutboxOptions>)null!))
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
        var options = provider.GetRequiredService<IOptions<SQLiteOutboxOptions>>();

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
        var options = provider.GetRequiredService<IOptions<SQLiteOutboxOptions>>();

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
}
