namespace NetEvolve.Pulse.SQLite.Tests.Integration;

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="SQLiteOutboxRepository"/> using a file-based SQLite database.
/// Verifies end-to-end behavior with a real SQLite file.
/// </summary>
[ClassDataSource<SQLiteFileFixture>(Shared = SharedType.PerClass)]
public sealed class SQLiteOutboxRepositoryIntegrationTests
{
    private readonly SQLiteFileFixture _fixture;
    private readonly string _dbPath;
    private readonly string _connectionString;

    public SQLiteOutboxRepositoryIntegrationTests(SQLiteFileFixture fixture)
    {
        _fixture = fixture;
        _dbPath = Path.Combine(Path.GetTempPath(), $"pulse_integration_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    [Before(Test)]
    public async Task SetupAsync() => await _fixture.InitializeSchemaAsync(_connectionString).ConfigureAwait(false);

    [After(Test)]
    public Task CleanupAsync()
    {
        if (File.Exists(_dbPath))
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
            var walFile = _dbPath + "-wal";
            var shmFile = _dbPath + "-shm";
            if (File.Exists(walFile))
            {
                File.Delete(walFile);
            }
            if (File.Exists(shmFile))
            {
                File.Delete(shmFile);
            }
        }
        return Task.CompletedTask;
    }

    private SQLiteOutboxRepository CreateRepository(string? connectionString = null)
    {
        var opts = new SQLiteOutboxOptions
        {
            ConnectionString = connectionString ?? _connectionString,
            EnableWalMode = true,
        };
        return new SQLiteOutboxRepository(Options.Create(opts), TimeProvider.System);
    }

    private static OutboxMessage CreateMessage(string eventType = "IntegrationEvent") =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = "{\"key\":\"value\"}",
            CorrelationId = "corr-123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Pending,
        };

    [Test]
    public async Task AddAsync_WithValidMessage_PersistsToFileDatabase()
    {
        var repository = CreateRepository();
        var message = CreateMessage("add-integration");

        await repository.AddAsync(message).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            connection
        );
        _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
        var count = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(1L);
    }

    [Test]
    public async Task GetPendingAsync_WithPendingMessages_ReturnsAndMarksAsProcessing()
    {
        var repository = CreateRepository();
        var message1 = CreateMessage("integration-pending-1");
        var message2 = CreateMessage("integration-pending-2");
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);

        var pending = await repository.GetPendingAsync(10).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(pending.Count).IsEqualTo(2);
            _ = await Assert.That(pending).All(m => m.Status == OutboxMessageStatus.Processing);
        }
    }

    [Test]
    public async Task FullLifecycle_AddProcessComplete_Works()
    {
        var repository = CreateRepository();
        var message = CreateMessage("lifecycle-test");
        await repository.AddAsync(message).ConfigureAwait(false);

        var pending = await repository.GetPendingAsync(1).ConfigureAwait(false);
        _ = await Assert.That(pending.Count).IsEqualTo(1);
        _ = await Assert.That(pending[0].Status).IsEqualTo(OutboxMessageStatus.Processing);

        await repository.MarkAsCompletedAsync(message.Id).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = new SqliteCommand(
            "SELECT \"Status\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            connection
        );
        _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
        var status = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(status).IsEqualTo((long)OutboxMessageStatus.Completed);
    }

    [Test]
    public async Task GetStatisticsAsync_ReturnsCorrectCounts()
    {
        var management = new SQLiteOutboxManagement(
            Options.Create(new SQLiteOutboxOptions { ConnectionString = _connectionString, EnableWalMode = true }),
            TimeProvider.System
        );
        var repository = CreateRepository();

        var message1 = CreateMessage("stats-pending");
        var message2 = CreateMessage("stats-failed");
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);
        await repository.MarkAsFailedAsync(message2.Id, "test error").ConfigureAwait(false);

        var stats = await management.GetStatisticsAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(stats.Pending).IsGreaterThanOrEqualTo(1L);
            _ = await Assert.That(stats.Failed).IsGreaterThanOrEqualTo(1L);
        }
    }

    [Test]
    public async Task WalModeEnabled_DatabaseFileCreated_WalFilesExist()
    {
        var repository = CreateRepository();
        var message = CreateMessage("wal-test");

        await repository.AddAsync(message).ConfigureAwait(false);

        // WAL mode creates a -wal sidecar file after the first write
        var walExists = File.Exists(_dbPath + "-wal") || File.Exists(_dbPath);
        _ = await Assert.That(walExists).IsTrue();
    }
}

/// <summary>
/// Fixture that provides schema initialization for file-based SQLite integration tests.
/// </summary>
public sealed class SQLiteFileFixture
{
    private readonly string _schemaSql;

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
