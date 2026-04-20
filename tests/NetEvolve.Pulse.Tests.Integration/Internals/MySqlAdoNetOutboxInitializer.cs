namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Configures the MySQL ADO.NET outbox provider for integration tests.
/// Executes <c>Scripts/MySql/OutboxMessage.sql</c> to create the required table before each test.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is read from a script file with table name substituted from validated OutboxOptions properties."
)]
public sealed class MySqlAdoNetOutboxInitializer : IDatabaseInitializer
{
    private static readonly string _scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "MySql",
        "OutboxMessage.sql"
    );

    /// <inheritdoc />
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture databaseService)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        _ = mediatorBuilder.AddMySqlOutbox(databaseService.ConnectionString);
    }

    /// <inheritdoc />
    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("OutboxOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? OutboxMessageSchema.DefaultTableName
            : options.TableName;

        var script = await File.ReadAllTextAsync(_scriptPath, cancellationToken).ConfigureAwait(false);

        // Replace the default table name with the actual one used for this test
        script = script.Replace(
            $"`{OutboxMessageSchema.DefaultTableName}`",
            $"`{tableName}`",
            StringComparison.Ordinal
        );

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Execute each SQL statement individually (CREATE TABLE, CREATE INDEX x3)
        // splitting on ";" avoids multi-statement connection string requirements.
        foreach (
            var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        )
        {
            // Skip comment-only blocks and empty lines
            if (IsCommentOrEmpty(statement))
            {
                continue;
            }

            await using var command = new MySqlCommand(statement, connection);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Initialize(IServiceCollection services, IServiceFixture databaseService)
    {
        // No additional service initialization required for ADO.NET outbox tests.
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
