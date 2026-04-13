namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is read from a script file with schema and table names substituted from validated OutboxOptions properties."
)]
public sealed class SqlServerAdoNetInitializer : IDatabaseInitializer
{
    private static readonly string _scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "SqlServer",
        "OutboxMessage.sql"
    );

    public void Configure(IMediatorBuilder mediatorBuilder, IDatabaseServiceFixture databaseService)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        mediatorBuilder.AddSqlServerOutbox(databaseService.ConnectionString);
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

        // Remove SQLCMD-specific variable declarations (not valid T-SQL)
        script = System.Text.RegularExpressions.Regex.Replace(
            script,
            @"^:setvar\s+\w+\s+.*$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline
        );

        // Substitute SQLCMD variables with actual values
        script = script
            .Replace("$(SchemaName)", schema, StringComparison.Ordinal)
            .Replace("$(TableName)", tableName, StringComparison.Ordinal);

        // Split on GO (on its own line) and execute each batch independently
        var batches = System.Text.RegularExpressions.Regex.Split(
            script,
            @"^\s*GO\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline
                | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

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

    public void Initialize(IServiceCollection services, IDatabaseServiceFixture databaseService) { }
}
