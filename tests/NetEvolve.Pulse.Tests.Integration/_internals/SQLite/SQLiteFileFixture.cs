namespace NetEvolve.Pulse.Tests.Integration.Internals;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

/// <summary>
/// Fixture that provides schema initialization for file-based SQLite integration tests.
/// </summary>
public sealed class SQLiteFileFixture
{
    private readonly string _schemaSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteFileFixture"/> class.
    /// </summary>
    public SQLiteFileFixture() =>
        _schemaSql = """
            CREATE TABLE IF NOT EXISTS "OutboxMessage"
            (
                "Id"            TEXT    NOT NULL,
                "EventType"     TEXT    NOT NULL,
                "Payload"       TEXT    NOT NULL,
                "CorrelationId" TEXT    NULL,
                "CreatedAt"     TEXT    NOT NULL,
                "UpdatedAt"     TEXT    NOT NULL,
                "ProcessedAt"   TEXT    NULL,
                "NextRetryAt"   TEXT    NULL,
                "RetryCount"    INTEGER NOT NULL DEFAULT 0,
                "Error"         TEXT    NULL,
                "Status"        INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "PK_OutboxMessage" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_OutboxMessage_Status_CreatedAt"
                ON "OutboxMessage" ("Status", "CreatedAt")
                WHERE "Status" IN (0, 3);
            CREATE INDEX IF NOT EXISTS "IX_OutboxMessage_Status_ProcessedAt"
                ON "OutboxMessage" ("Status", "ProcessedAt")
                WHERE "Status" = 2;
            """;

    /// <summary>
    /// Initializes the outbox schema on the given SQLite database file.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string for the target database file.</param>
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Schema DDL is a constant string with no user input."
    )]
    public async Task InitializeSchemaAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = _schemaSql;
        _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
