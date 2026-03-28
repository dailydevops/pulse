namespace NetEvolve.Pulse.EntityFramework.Tests.Integration;

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Unit-style tests for <see cref="OutboxMessageConfigurationFactory"/> and the
/// provider-specific configuration classes. No database connection is required.
/// </summary>
public sealed class OutboxMessageConfigurationFactoryTests
{
    [Test]
    [Arguments("Microsoft.EntityFrameworkCore.SqlServer", typeof(SqlServerOutboxMessageConfiguration))]
    [Arguments("Npgsql.EntityFrameworkCore.PostgreSQL", typeof(PostgreSqlOutboxMessageConfiguration))]
    [Arguments("Microsoft.EntityFrameworkCore.Sqlite", typeof(SqliteOutboxMessageConfiguration))]
    [Arguments("Pomelo.EntityFrameworkCore.MySql", typeof(MySqlOutboxMessageConfiguration))]
    [Arguments("MySql.EntityFrameworkCore", typeof(MySqlOutboxMessageConfiguration))]
    public async Task Create_ByProviderName_ReturnsExpectedConfigurationType(string providerName, Type expectedType)
    {
        var result = OutboxMessageConfigurationFactory.Create(providerName);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result.GetType()).IsEqualTo(expectedType);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Microsoft.EntityFrameworkCore.Unknown")]
    public async Task Create_ByProviderName_WithUnsupportedProvider_ThrowsNotSupportedException(string? providerName) =>
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Task.FromResult(OutboxMessageConfigurationFactory.Create(providerName))
        );

    [Test]
    [Arguments("Microsoft.EntityFrameworkCore.SqlServer", typeof(SqlServerOutboxMessageConfiguration))]
    [Arguments("Npgsql.EntityFrameworkCore.PostgreSQL", typeof(PostgreSqlOutboxMessageConfiguration))]
    [Arguments("Microsoft.EntityFrameworkCore.Sqlite", typeof(SqliteOutboxMessageConfiguration))]
    [Arguments("Pomelo.EntityFrameworkCore.MySql", typeof(MySqlOutboxMessageConfiguration))]
    [Arguments("MySql.EntityFrameworkCore", typeof(MySqlOutboxMessageConfiguration))]
    public async Task Create_ByProviderName_WithOptions_ReturnsExpectedConfigurationType(
        string providerName,
        Type expectedType
    )
    {
        var options = Options.Create(new OutboxOptions { Schema = "custom" });

        var result = OutboxMessageConfigurationFactory.Create(providerName, options);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result.GetType()).IsEqualTo(expectedType);
    }

    [Test]
    public async Task Create_WithNullContext_ThrowsArgumentNullException()
    {
        DbContext? context = null;

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(OutboxMessageConfigurationFactory.Create(context!))
        );
    }

    [Test]
    public async Task SqlServerConfiguration_PendingFilter_UsesBracketQuoting()
    {
        var config = new SqlServerOutboxMessageConfiguration();

        var filter = GetPendingFilter(config);

        _ = await Assert.That(filter).IsNotNull();
        _ = await Assert.That(filter).Contains($"[{OutboxMessageSchema.Columns.Status}]");
        _ = await Assert.That(filter).Contains("0");
        _ = await Assert.That(filter).Contains("3");
    }

    [Test]
    public async Task SqlServerConfiguration_CompletedFilter_UsesBracketQuoting()
    {
        var config = new SqlServerOutboxMessageConfiguration();

        var filter = GetCompletedFilter(config);

        _ = await Assert.That(filter).IsNotNull();
        _ = await Assert.That(filter).Contains($"[{OutboxMessageSchema.Columns.Status}]");
        _ = await Assert.That(filter).Contains("2");
    }

    [Test]
    public async Task PostgreSqlConfiguration_PendingFilter_UsesDoubleQuoting()
    {
        var config = new PostgreSqlOutboxMessageConfiguration();

        var filter = GetPendingFilter(config);

        _ = await Assert.That(filter).IsNotNull();
        _ = await Assert.That(filter).Contains($"\"{OutboxMessageSchema.Columns.Status}\"");
        _ = await Assert.That(filter).Contains("0");
        _ = await Assert.That(filter).Contains("3");
    }

    [Test]
    public async Task PostgreSqlConfiguration_CompletedFilter_UsesDoubleQuoting()
    {
        var config = new PostgreSqlOutboxMessageConfiguration();

        var filter = GetCompletedFilter(config);

        _ = await Assert.That(filter).IsNotNull();
        _ = await Assert.That(filter).Contains($"\"{OutboxMessageSchema.Columns.Status}\"");
        _ = await Assert.That(filter).Contains("2");
    }

    [Test]
    public async Task SqliteConfiguration_PendingFilter_UsesDoubleQuoting()
    {
        var config = new SqliteOutboxMessageConfiguration();

        var filter = GetPendingFilter(config);

        _ = await Assert.That(filter).IsNotNull();
        _ = await Assert.That(filter).Contains($"\"{OutboxMessageSchema.Columns.Status}\"");
        _ = await Assert.That(filter).Contains("0");
        _ = await Assert.That(filter).Contains("3");
    }

    [Test]
    public async Task SqliteConfiguration_CompletedFilter_UsesDoubleQuoting()
    {
        var config = new SqliteOutboxMessageConfiguration();

        var filter = GetCompletedFilter(config);

        _ = await Assert.That(filter).IsNotNull();
        _ = await Assert.That(filter).Contains($"\"{OutboxMessageSchema.Columns.Status}\"");
        _ = await Assert.That(filter).Contains("2");
    }

    [Test]
    public async Task MySqlConfiguration_PendingFilter_IsNull()
    {
        var config = new MySqlOutboxMessageConfiguration();

        var filter = GetPendingFilter(config);

        _ = await Assert.That(filter).IsNull();
    }

    [Test]
    public async Task MySqlConfiguration_CompletedFilter_IsNull()
    {
        var config = new MySqlOutboxMessageConfiguration();

        var filter = GetCompletedFilter(config);

        _ = await Assert.That(filter).IsNull();
    }

    private static string? GetPendingFilter(OutboxMessageConfigurationBase config)
    {
        var prop = typeof(OutboxMessageConfigurationBase).GetProperty(
            "PendingMessagesFilter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        return (string?)prop!.GetValue(config);
    }

    private static string? GetCompletedFilter(OutboxMessageConfigurationBase config)
    {
        var prop = typeof(OutboxMessageConfigurationBase).GetProperty(
            "CompletedMessagesFilter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        return (string?)prop!.GetValue(config);
    }
}
