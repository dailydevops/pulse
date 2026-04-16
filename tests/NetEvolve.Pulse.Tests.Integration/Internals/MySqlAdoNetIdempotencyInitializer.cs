namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Configures the MySQL ADO.NET idempotency store provider for integration tests.
/// Executes <c>Scripts/MySql/IdempotencyKey.sql</c> to create the required table before each test.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is read from a script file with table name substituted from validated IdempotencyKeyOptions properties."
)]
public sealed class MySqlAdoNetIdempotencyInitializer : IDatabaseInitializer
{
    private static readonly string _scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "MySql",
        "IdempotencyKey.sql"
    );

    /// <inheritdoc />
    public void Configure(IMediatorBuilder mediatorBuilder, IDatabaseServiceFixture databaseService)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        _ = mediatorBuilder.AddMySqlIdempotencyStore(databaseService.ConnectionString);
    }

    /// <inheritdoc />
    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetRequiredService<IOptions<IdempotencyKeyOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("IdempotencyKeyOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? IdempotencyKeySchema.DefaultTableName
            : options.TableName;

        var script = await File.ReadAllTextAsync(_scriptPath, cancellationToken).ConfigureAwait(false);

        // Replace the default table name with the actual one used for this test
        script = script.Replace(
            $"`{IdempotencyKeySchema.DefaultTableName}`",
            $"`{tableName}`",
            StringComparison.Ordinal
        );

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Execute each SQL statement individually (CREATE TABLE, CREATE INDEX)
        foreach (
            var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        )
        {
            if (IsCommentOrEmpty(statement))
            {
                continue;
            }

            await using var command = new MySqlCommand(statement, connection);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Initialize(IServiceCollection services, IDatabaseServiceFixture databaseService)
    {
        // No additional service initialization required for ADO.NET idempotency tests.
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
