namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is constructed from validated OutboxOptions.TableName property, not user input."
)]
public sealed class SQLiteAdoNetOutboxInitializer : IDatabaseInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IDatabaseServiceFixture databaseService)
    {
        ArgumentNullException.ThrowIfNull(mediatorBuilder);
        ArgumentNullException.ThrowIfNull(databaseService);
        _ = mediatorBuilder.AddSQLiteOutbox(databaseService.ConnectionString);
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var options = serviceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("OutboxOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? OutboxMessageSchema.DefaultTableName
            : options.TableName;

        var quotedTable = $"\"{tableName}\"";
        var quotedPk = $"\"PK_{tableName}\"";
        var quotedIdx1 = $"\"IX_{tableName}_Status_CreatedAt\"";
        var quotedIdx2 = $"\"IX_{tableName}_Status_ProcessedAt\"";

        var createTableSql = $"""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS {quotedTable} (
                "Id"            TEXT        NOT NULL,
                "EventType"     TEXT        NOT NULL,
                "Payload"       TEXT        NOT NULL,
                "CorrelationId" TEXT        NULL,
                "CreatedAt"     TEXT        NOT NULL,
                "UpdatedAt"     TEXT        NOT NULL,
                "ProcessedAt"   TEXT        NULL,
                "NextRetryAt"   TEXT        NULL,
                "RetryCount"    INTEGER     NOT NULL DEFAULT 0,
                "Error"         TEXT        NULL,
                "Status"        INTEGER     NOT NULL DEFAULT 0,
                CONSTRAINT {quotedPk} PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS {quotedIdx1}
                ON {quotedTable} ("Status", "CreatedAt") WHERE "Status" IN (0, 3);
            CREATE INDEX IF NOT EXISTS {quotedIdx2}
                ON {quotedTable} ("Status", "ProcessedAt") WHERE "Status" = 2;
            """;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqliteCommand(createTableSql, connection);
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Initialize(IServiceCollection services, IDatabaseServiceFixture databaseService) { }
}
