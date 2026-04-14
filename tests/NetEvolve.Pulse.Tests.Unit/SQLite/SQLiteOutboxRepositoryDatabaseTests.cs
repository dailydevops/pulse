namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Database-level unit tests for <see cref="SQLiteOutboxRepository"/> using an in-memory SQLite database.
/// </summary>
[TestGroup("SQLite")]
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
        var options = Options.Create(new OutboxOptions { ConnectionString = _connectionString, EnableWalMode = false });
        return new SQLiteOutboxRepository(options, TimeProvider.System);
    }

    private SQLiteOutboxRepository CreateRepositoryWithScope(IOutboxTransactionScope scope)
    {
        var options = Options.Create(new OutboxOptions { ConnectionString = _connectionString, EnableWalMode = false });
        return new SQLiteOutboxRepository(options, TimeProvider.System, scope);
    }

    private static OutboxMessage CreateMessage(Type? eventType = null) =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType ?? typeof(TestSQLiteRepoEvent),
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Pending,
        };

    [Test]
    public async Task AddAsync_WithValidMessage_PersistsToDatabase(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        var message = CreateMessage();

        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var cmd = new SqliteCommand("SELECT COUNT(*) FROM \"OutboxMessage\" WHERE \"Id\" = @Id", _keepAlive);
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
            var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

            _ = await Assert.That(count).IsEqualTo(1L);
        }
    }

    [Test]
    public async Task GetPendingAsync_WithPendingMessages_ReturnsAndMarksAsProcessing(
        CancellationToken cancellationToken
    )
    {
        var repository = CreateRepository();
        var message1 = CreateMessage(typeof(TestSQLiteRepoEvent));
        var message2 = CreateMessage();
        await repository.AddAsync(message1, cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(message2, cancellationToken).ConfigureAwait(false);

        var pending = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(pending.Count).IsEqualTo(2);
            _ = await Assert.That(pending).All(m => m.Status == OutboxMessageStatus.Processing);
        }
    }

    [Test]
    public async Task GetPendingAsync_WithEmptyTable_ReturnsEmptyList(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();

        var pending = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(pending.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MarkAsCompletedAsync_SetsStatusToCompleted(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await repository.MarkAsCompletedAsync(message.Id, cancellationToken).ConfigureAwait(false);

        var cmd = new SqliteCommand("SELECT \"Status\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id", _keepAlive);
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
            var status = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            _ = await Assert.That(status).IsEqualTo((long)OutboxMessageStatus.Completed);
        }
    }

    [Test]
    public async Task MarkAsFailedAsync_SetsStatusToFailed(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await repository
            .MarkAsFailedAsync(message.Id, "Test error", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var cmd = new SqliteCommand(
            "SELECT \"Status\", \"Error\", \"RetryCount\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            _keepAlive
        );
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
            var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                _ = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                using (Assert.Multiple())
                {
                    _ = await Assert.That(reader.GetInt64(0)).IsEqualTo((long)OutboxMessageStatus.Failed);
                    _ = await Assert.That(reader.GetString(1)).IsEqualTo("Test error");
                    _ = await Assert.That(reader.GetInt64(2)).IsEqualTo(1L);
                }
            }
        }
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_SetsStatusToDeadLetter(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await repository.MarkAsDeadLetterAsync(message.Id, "Fatal error", cancellationToken).ConfigureAwait(false);

        var cmd = new SqliteCommand("SELECT \"Status\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id", _keepAlive);
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
            var status = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            _ = await Assert.That(status).IsEqualTo((long)OutboxMessageStatus.DeadLetter);
        }
    }

    [Test]
    public async Task GetPendingCountAsync_WithPendingMessages_ReturnsCorrectCount(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);

        var count = await repository.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(count).IsGreaterThanOrEqualTo(2L);
    }

    [Test]
    public async Task DeleteCompletedAsync_DeletesOldCompletedMessages(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);
        await repository.MarkAsCompletedAsync(message.Id, cancellationToken).ConfigureAwait(false);

        var deleted = await repository.DeleteCompletedAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(deleted).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task GetFailedForRetryAsync_WithFailedMessages_ReturnsEligibleMessages(
        CancellationToken cancellationToken
    )
    {
        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);
        await repository
            .MarkAsFailedAsync(message.Id, "First failure", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var forRetry = await repository
            .GetFailedForRetryAsync(maxRetryCount: 3, batchSize: 10, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(forRetry.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task MarkAsFailedAsync_WithNextRetryAt_SetsNextRetryAt(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var nextRetry = DateTimeOffset.UtcNow.AddMinutes(5);
        await repository
            .MarkAsFailedAsync(message.Id, "Error with retry", nextRetry, cancellationToken)
            .ConfigureAwait(false);

        var cmd = new SqliteCommand("SELECT \"NextRetryAt\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id", _keepAlive);
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
            var nextRetryValue = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(nextRetryValue).IsNotNull();
        }
    }

    [Test]
    public async Task AddAsync_UsesAmbientTransactionScope(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var transaction = (SqliteTransaction)
                await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var scope = new StubTransactionScope(transaction);
                var repository = CreateRepositoryWithScope(scope);
                var message = CreateMessage();

                await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

                await using var cmd = new SqliteCommand(
                    "SELECT COUNT(*) FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
                    _keepAlive
                );
                _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
                var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

                _ = await Assert.That(count).IsEqualTo(0L);
            }
        }
    }

    private sealed class StubTransactionScope(SqliteTransaction transaction) : IOutboxTransactionScope
    {
        public object? GetCurrentTransaction() => transaction;
    }

    private sealed record TestSQLiteRepoEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
