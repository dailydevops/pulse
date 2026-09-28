namespace NetEvolve.Pulse.Tests.Integration.Internals.Audit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Configures the MySQL ADO.NET audit trail store provider for integration tests.
/// Executes <c>Scripts/MySql/AuditEntry.sql</c> to create the required table before each test.
/// </summary>
public sealed class MySqlAdoNetAuditInitializer : IServiceInitializer
{
    /// <inheritdoc />
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _ = mediatorBuilder.AddMySqlAuditStore(options => options.ConnectionString = serviceFixture.ConnectionString);
    }

    /// <inheritdoc />
    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<AuditStoreOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("AuditStoreOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? AuditEntrySchema.DefaultTableName
            : options.TableName;

        await MySqlScriptRunner
            .ExecuteAsync(
                connectionString,
                "AuditEntry.sql",
                AuditEntrySchema.DefaultTableName,
                tableName,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET audit trail tests.
    }
}
