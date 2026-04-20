namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;

[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is constructed from validated IdempotencyKeyOptions.TableName property, not user input."
)]
public sealed class SQLiteAdoNetIdempotencyInitializer : IDatabaseInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture databaseService)
    {
        ArgumentNullException.ThrowIfNull(mediatorBuilder);
        ArgumentNullException.ThrowIfNull(databaseService);
        _ = mediatorBuilder.AddSQLiteIdempotencyStore(databaseService.ConnectionString);
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var options = serviceProvider.GetRequiredService<IOptions<IdempotencyKeyOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("IdempotencyKeyOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? IdempotencyKeySchema.DefaultTableName
            : options.TableName;

        var quotedTable = $"\"{tableName}\"";
        var quotedPk = $"\"PK_{tableName}\"";
        var quotedIdx = $"\"IX_{tableName}_CreatedAt\"";

        var createTableSql = $"""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS {quotedTable} (
                "IdempotencyKey" TEXT NOT NULL,
                "CreatedAt"      TEXT NOT NULL,
                CONSTRAINT {quotedPk} PRIMARY KEY ("IdempotencyKey")
            );
            CREATE INDEX IF NOT EXISTS {quotedIdx}
                ON {quotedTable} ("CreatedAt");
            """;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqliteCommand(createTableSql, connection);
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Initialize(IServiceCollection services, IServiceFixture databaseService) { }
}
