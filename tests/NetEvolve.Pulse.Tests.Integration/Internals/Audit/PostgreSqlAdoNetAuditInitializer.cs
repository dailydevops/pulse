namespace NetEvolve.Pulse.Tests.Integration.Internals.Audit;

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;
using Npgsql;

[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is read from a script file with schema and table names substituted from validated AuditStoreOptions properties."
)]
public sealed partial class PostgreSqlAdoNetAuditInitializer : IServiceInitializer
{
    private static readonly string _scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "PostgreSql",
        "AuditEntry.sql"
    );

    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _ = mediatorBuilder.AddPostgreSqlAuditStore(options =>
            options.ConnectionString = serviceFixture.ConnectionString
        );
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = serviceProvider.GetRequiredService<IOptions<AuditStoreOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("AuditStoreOptions.ConnectionString is not configured.");

        var schema = string.IsNullOrWhiteSpace(options.Schema) ? AuditEntrySchema.DefaultSchema : options.Schema;

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? AuditEntrySchema.DefaultTableName
            : options.TableName;

        var script = await File.ReadAllTextAsync(_scriptPath, cancellationToken).ConfigureAwait(false);

        // Remove psql-specific variable declarations (not valid SQL)
        script = SearchSetVar().Replace(script, string.Empty);

        // Substitute psql variables with actual values.
        script = script
            .Replace(":schema_name", schema, StringComparison.Ordinal)
            .Replace(":table_name", tableName, StringComparison.Ordinal);

        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(script, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET audit trail tests.
        // The Configure method handles all necessary service registrations.
    }

    [GeneratedRegex(@"^\\set\s+\w+\s+.*$", RegexOptions.Multiline, 10000)]
    private static partial Regex SearchSetVar();
}
