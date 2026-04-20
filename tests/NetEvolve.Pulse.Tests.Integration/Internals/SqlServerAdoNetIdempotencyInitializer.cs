namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Idempotency;

[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is read from a script file with schema and table names substituted from validated IdempotencyKeyOptions properties."
)]
public sealed partial class SqlServerAdoNetIdempotencyInitializer : IDatabaseInitializer
{
    private static readonly string _scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "SqlServer",
        "IdempotencyKey.sql"
    );

    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture databaseService)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        _ = mediatorBuilder.AddSqlServerIdempotencyStore(databaseService.ConnectionString);
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<IdempotencyKeyOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("IdempotencyKeyOptions.ConnectionString is not configured.");

        var schema = string.IsNullOrWhiteSpace(options.Schema)
            ? Extensibility.Idempotency.IdempotencyKeySchema.DefaultSchema
            : options.Schema;

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? Extensibility.Idempotency.IdempotencyKeySchema.DefaultTableName
            : options.TableName;

        var script = await File.ReadAllTextAsync(_scriptPath, cancellationToken).ConfigureAwait(false);

        // Remove SQLCMD-specific variable declarations (not valid T-SQL)
        script = SearchSetVar().Replace(script, string.Empty);

        // Substitute SQLCMD variables with actual values
        script = script
            .Replace("$(SchemaName)", schema, StringComparison.Ordinal)
            .Replace("$(TableName)", tableName, StringComparison.Ordinal);

        // Split on GO (on its own line) and execute each batch independently
        var batches = SearchGoStatements().Split(script);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var batch in batches)
        {
            var trimmed = batch.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            await using var command = new SqlCommand(trimmed, connection);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Initialize(IServiceCollection services, IServiceFixture databaseService) { }

    [GeneratedRegex(@"^:setvar\s+\w+\s+.*$", RegexOptions.Multiline, 10000)]
    private static partial Regex SearchSetVar();

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline, 10000)]
    private static partial Regex SearchGoStatements();
}
