namespace NetEvolve.Pulse.Tests.Integration.Internals.Audit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Configures the MySQL ADO.NET audit trail store provider for integration tests.
/// Executes <c>Scripts/MySql/AuditEntry.sql</c> to create the required table before each test.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is read from a script file with table name substituted from validated AuditStoreOptions properties."
)]
public sealed class MySqlAdoNetAuditInitializer : IServiceInitializer
{
    private static readonly string _scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "MySql",
        "AuditEntry.sql"
    );

    /// <inheritdoc />
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _ = mediatorBuilder.AddMySqlAuditStore(options => options.ConnectionString = serviceFixture.ConnectionString);
    }

    /// <inheritdoc />
    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = serviceProvider.GetRequiredService<IOptions<AuditStoreOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("AuditStoreOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? AuditEntrySchema.DefaultTableName
            : options.TableName;

        var script = await File.ReadAllTextAsync(_scriptPath, cancellationToken).ConfigureAwait(false);

        // Replace only the table name occurrences (CREATE TABLE and ON clauses),
        // not the identically-named column definition.
        script = script
            .Replace(
                $"TABLE IF NOT EXISTS `{AuditEntrySchema.DefaultTableName}`",
                $"TABLE IF NOT EXISTS `{tableName}`",
                StringComparison.Ordinal
            )
            .Replace(
                $"\n    ON `{AuditEntrySchema.DefaultTableName}`",
                $"\n    ON `{tableName}`",
                StringComparison.Ordinal
            )
            .Replace(
                $"CONSTRAINT `PK_{AuditEntrySchema.DefaultTableName}`",
                $"CONSTRAINT `PK_{tableName}`",
                StringComparison.Ordinal
            );

        var connection = new MySqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Execute each SQL statement individually (CREATE TABLE, CREATE INDEX)
            foreach (
                var statement in script.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                if (IsCommentOrEmpty(statement))
                {
                    continue;
                }

                var command = new MySqlCommand(statement, connection);
                await using (command.ConfigureAwait(false))
                {
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        // No additional service initialization required for ADO.NET audit trail tests.
    }

    private static bool IsCommentOrEmpty(string statement)
    {
        foreach (var line in statement.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
