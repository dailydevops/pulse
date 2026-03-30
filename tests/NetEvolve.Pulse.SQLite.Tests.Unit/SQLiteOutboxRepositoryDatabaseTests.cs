namespace NetEvolve.Pulse.SQLite.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Database-level unit tests for <see cref="SQLiteOutboxRepository"/> using an in-memory SQLite database.
/// </summary>
public sealed class SQLiteOutboxRepositoryDatabaseTests : IAsyncDisposable
{
    // Named shared in-memory database - unique per test instance
    private readonly string _dbName = $"unit_{Guid.NewGuid():N}";
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;

    public SQLiteOutboxRepositoryDatabaseTests()
    {
        _connectionString = $"Data Source={_dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        ApplySchema();
    }

    public async ValueTask DisposeAsync() => await _keepAlive.DisposeAsync().ConfigureAwait(false);

    private void ApplySchema()
    {
        using var cmd = new SqliteCommand(
            """
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
            """,
            _keepAlive
        );
        _ = cmd.ExecuteNonQuery();
    }

    private SQLiteOutboxRepository CreateRepository()
    {
        var options = Options.Create(
            new SQLiteOutboxOptions { ConnectionString = _connectionString, EnableWalMode = false }
        );
        return new SQLiteOutboxRepository(options, TimeProvider.System);
    }

    private static OutboxMessage CreateMessage(string eventType = "TestEvent") =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Pending,
        };

    [Test]
    public async Task AddAsync_WithValidMessage_PersistsToDatabase()
    {
        var repository = CreateRepository();
        var message = CreateMessage();

        await repository.AddAsync(message).ConfigureAwait(false);

        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            _keepAlive
        );
        _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
        var count = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(1L);
    }

    [Test]
    public async Task GetPendingAsync_WithPendingMessages_ReturnsAndMarksAsProcessing()
    {
        var repository = CreateRepository();
        var message1 = CreateMessage("pending-event-1");
        var message2 = CreateMessage("pending-event-2");
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
    public async Task GetPendingAsync_WithEmptyTable_ReturnsEmptyList()
    {
        var repository = CreateRepository();

        var pending = await repository.GetPendingAsync(10).ConfigureAwait(false);

        _ = await Assert.That(pending.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MarkAsCompletedAsync_SetsStatusToCompleted()
    {
        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message).ConfigureAwait(false);

        await repository.MarkAsCompletedAsync(message.Id).ConfigureAwait(false);

        await using var cmd = new SqliteCommand(
            "SELECT \"Status\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            _keepAlive
        );
        _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
        var status = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        _ = await Assert.That(status).IsEqualTo((long)OutboxMessageStatus.Completed);
    }

    [Test]
    public async Task MarkAsFailedAsync_SetsStatusToFailed()
    {
        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message).ConfigureAwait(false);

        await repository.MarkAsFailedAsync(message.Id, "Test error").ConfigureAwait(false);

        await using var cmd = new SqliteCommand(
            "SELECT \"Status\", \"Error\", \"RetryCount\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            _keepAlive
        );
        _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        _ = await reader.ReadAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reader.GetInt64(0)).IsEqualTo((long)OutboxMessageStatus.Failed);
            _ = await Assert.That(reader.GetString(1)).IsEqualTo("Test error");
            _ = await Assert.That(reader.GetInt64(2)).IsEqualTo(1L);
        }
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_SetsStatusToDeadLetter()
    {
        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message).ConfigureAwait(false);

        await repository.MarkAsDeadLetterAsync(message.Id, "Fatal error").ConfigureAwait(false);

        await using var cmd = new SqliteCommand(
            "SELECT \"Status\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            _keepAlive
        );
        _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
        var status = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        _ = await Assert.That(status).IsEqualTo((long)OutboxMessageStatus.DeadLetter);
    }

    [Test]
    public async Task GetPendingCountAsync_WithPendingMessages_ReturnsCorrectCount()
    {
        var repository = CreateRepository();
        await repository.AddAsync(CreateMessage("count-1")).ConfigureAwait(false);
        await repository.AddAsync(CreateMessage("count-2")).ConfigureAwait(false);

        var count = await repository.GetPendingCountAsync().ConfigureAwait(false);

        _ = await Assert.That(count).IsGreaterThanOrEqualTo(2L);
    }

    [Test]
    public async Task DeleteCompletedAsync_DeletesOldCompletedMessages()
    {
        var repository = CreateRepository();
        var message = CreateMessage("delete-test");
        await repository.AddAsync(message).ConfigureAwait(false);
        await repository.MarkAsCompletedAsync(message.Id).ConfigureAwait(false);

        var deleted = await repository.DeleteCompletedAsync(TimeSpan.Zero).ConfigureAwait(false);

        _ = await Assert.That(deleted).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task GetFailedForRetryAsync_WithFailedMessages_ReturnsEligibleMessages()
    {
        var repository = CreateRepository();
        var message = CreateMessage("retry-test");
        await repository.AddAsync(message).ConfigureAwait(false);
        await repository.MarkAsFailedAsync(message.Id, "First failure").ConfigureAwait(false);

        var forRetry = await repository.GetFailedForRetryAsync(maxRetryCount: 3, batchSize: 10).ConfigureAwait(false);

        _ = await Assert.That(forRetry.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task MarkAsFailedAsync_WithNextRetryAt_SetsNextRetryAt()
    {
        var repository = CreateRepository();
        var message = CreateMessage("retry-at-test");
        await repository.AddAsync(message).ConfigureAwait(false);

        var nextRetry = DateTimeOffset.UtcNow.AddMinutes(5);
        await repository.MarkAsFailedAsync(message.Id, "Error with retry", nextRetry).ConfigureAwait(false);

        await using var cmd = new SqliteCommand(
            "SELECT \"NextRetryAt\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            _keepAlive
        );
        _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
        var nextRetryValue = await cmd.ExecuteScalarAsync().ConfigureAwait(false);

        _ = await Assert.That(nextRetryValue).IsNotNull();
    }
}
