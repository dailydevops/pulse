namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using Npgsql;

[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is read from a script file with schema and table names substituted from validated OutboxOptions properties."
)]
public sealed partial class PostgreSqlAdoNetOutboxInitializer : IDatabaseInitializer
{
    private static readonly string _scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "PostgreSql",
        "OutboxMessage.sql"
    );

    public void Configure(IMediatorBuilder mediatorBuilder, IDatabaseServiceFixture databaseService)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        _ = mediatorBuilder.AddPostgreSqlOutbox(databaseService.ConnectionString);
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("OutboxOptions.ConnectionString is not configured.");

        var schema = string.IsNullOrWhiteSpace(options.Schema) ? OutboxMessageSchema.DefaultSchema : options.Schema;

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? OutboxMessageSchema.DefaultTableName
            : options.TableName;

        var script = await File.ReadAllTextAsync(_scriptPath, cancellationToken).ConfigureAwait(false);

        // Remove psql-specific variable declarations (not valid SQL)
        script = SearchSetVar().Replace(script, string.Empty);

        // Substitute psql variables with actual values.
        // PostgreSQL script uses :schema_name and :table_name as placeholders.
        // The placeholders appear both unquoted (e.g., CREATE SCHEMA :schema_name)
        // and within quotes (e.g., ":schema_name".":table_name").
        // We replace all occurrences with the actual values directly.
        script = script
            .Replace(":schema_name", schema, StringComparison.Ordinal)
            .Replace(":table_name", tableName, StringComparison.Ordinal);

        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(script, connection);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Initialize(IServiceCollection services, IDatabaseServiceFixture databaseService)
    {
        // No additional service initialization required for ADO.NET outbox tests.
        // The Configure method handles all necessary service registrations.
    }

    [GeneratedRegex(@"^\\set\s+\w+\s+.*$", RegexOptions.Multiline, 10000)]
    private static partial Regex SearchSetVar();
}
