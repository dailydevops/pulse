namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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
                "CausationId"   TEXT    NULL,
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

    private SQLiteOutboxRepository CreateRepository(TimeProvider timeProvider, TimeSpan? processingLeaseTimeout = null)
    {
        var options = Options.Create(
            new OutboxOptions
            {
                ConnectionString = _connectionString,
                EnableWalMode = false,
                ProcessingLeaseTimeout = processingLeaseTimeout ?? TimeSpan.FromMinutes(5),
            }
        );
        return new SQLiteOutboxRepository(options, timeProvider);
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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

        var repository = CreateRepository();

        var pending = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(pending.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetPendingAsync_WhenProcessingLeaseExpired_ReclaimsStuckMessage(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var repository = CreateRepository(timeProvider, TimeSpan.FromMinutes(5));
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(claimed.Count).IsEqualTo(1);

        // Simulate a crashed worker: the message stays in Processing and is never completed or failed.
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        var reclaimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reclaimed.Count).IsEqualTo(1);
            _ = await Assert.That(reclaimed[0].Id).IsEqualTo(message.Id);
            _ = await Assert.That(reclaimed[0].Status).IsEqualTo(OutboxMessageStatus.Processing);
        }
    }

    [Test]
    public async Task GetPendingAsync_WhileProcessingLeaseActive_DoesNotReclaimMessage(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var repository = CreateRepository(timeProvider, TimeSpan.FromMinutes(5));
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(claimed.Count).IsEqualTo(1);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var reclaimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(reclaimed).IsEmpty();
    }

    [Test]
    public async Task MarkAsCompletedAsync_SetsStatusToCompleted(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

        var repository = CreateRepository();
        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);
        await repository.AddAsync(CreateMessage(), cancellationToken).ConfigureAwait(false);

        var count = await repository.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(count).IsGreaterThanOrEqualTo(2L);
    }

    [Test]
    public async Task DeleteCompletedAsync_DeletesOldCompletedMessages(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
    public async Task GetFailedForRetryAsync_WithDueNonUtcNextRetryAt_ReturnsMessage(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var nextRetry = DateTimeOffset.UtcNow.AddMinutes(-30).ToOffset(TimeSpan.FromHours(2));
        await repository
            .MarkAsFailedAsync(message.Id, "Failure with offset retry", nextRetry, cancellationToken)
            .ConfigureAwait(false);

        var forRetry = await repository
            .GetFailedForRetryAsync(maxRetryCount: 3, batchSize: 10, cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(forRetry.Count).IsEqualTo(1);
            _ = await Assert.That(forRetry).All(m => m.Id == message.Id);
        }
    }

    [Test]
    public async Task GetPendingAsync_WithDueNonUtcNextRetryAt_ReturnsMessage(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = CreateRepository();
        var message = CreateMessage();
        message.NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(-30).ToOffset(TimeSpan.FromHours(2));
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var pending = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(pending.Count).IsEqualTo(1);
            _ = await Assert.That(pending).All(m => m.Id == message.Id);
        }
    }

    [Test]
    public async Task AddAsync_UsesAmbientTransactionScope(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

                var cmd = new SqliteCommand("SELECT COUNT(*) FROM \"OutboxMessage\" WHERE \"Id\" = @Id", _keepAlive);
                await using (cmd.ConfigureAwait(false))
                {
                    _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
                    var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

                    _ = await Assert.That(count).IsEqualTo(0L);
                }
            }
        }
    }

    [Test]
    public async Task CreateConnection_WithWalModeEnabled_AppliesJournalModePragmaOnlyOnce(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dbPath = Path.Combine(Path.GetTempPath(), $"pulse_wal_{Guid.NewGuid():N}.db");
        var fileConnectionString = $"Data Source={dbPath};Pooling=False";

        try
        {
            var schemaConnection = new SqliteConnection(fileConnectionString);
            await using (schemaConnection.ConfigureAwait(false))
            {
                await schemaConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var schemaCmd = new SqliteCommand(
                    """
                    CREATE TABLE IF NOT EXISTS "OutboxMessage"
                    (
                        "Id"            TEXT    NOT NULL,
                        "EventType"     TEXT    NOT NULL,
                        "Payload"       TEXT    NOT NULL,
                        "CorrelationId" TEXT    NULL,
                        "CausationId"   TEXT    NULL,
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
                    schemaConnection
                );
                await using (schemaCmd.ConfigureAwait(false))
                {
                    _ = await schemaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            var options = Options.Create(
                new OutboxOptions { ConnectionString = fileConnectionString, EnableWalMode = true }
            );
            var repository = new SQLiteOutboxRepository(options, TimeProvider.System);

            _ = await repository.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);

            var afterFirstUse = await SetJournalModeDeleteAsync(fileConnectionString, cancellationToken)
                .ConfigureAwait(false);

            _ = await repository.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);

            var afterSecondUse = await GetJournalModeAsync(fileConnectionString, cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(afterFirstUse).IsEqualTo("delete");
                _ = await Assert.That(afterSecondUse).IsEqualTo("delete");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Test]
    public async Task MarkAsBatchOverloads_AreImplementedBySQLiteRepository(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var markCompleted = typeof(SQLiteOutboxRepository).GetMethod(
            nameof(IOutboxRepository.MarkAsCompletedAsync),
            [typeof(IReadOnlyCollection<Guid>), typeof(CancellationToken)]
        );
        var markFailed = typeof(SQLiteOutboxRepository).GetMethod(
            nameof(IOutboxRepository.MarkAsFailedAsync),
            [typeof(IReadOnlyCollection<Guid>), typeof(string), typeof(CancellationToken)]
        );
        var markDeadLetter = typeof(SQLiteOutboxRepository).GetMethod(
            nameof(IOutboxRepository.MarkAsDeadLetterAsync),
            [typeof(IReadOnlyCollection<Guid>), typeof(string), typeof(CancellationToken)]
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(markCompleted).IsNotNull();
            _ = await Assert.That(markFailed).IsNotNull();
            _ = await Assert.That(markDeadLetter).IsNotNull();
        }
    }

    [Test]
    public async Task MarkAsCompletedAsync_WithMultipleMessageIds_MarksAllAsCompleted(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IOutboxRepository repository = CreateRepository();
        var messages = new[] { CreateMessage(), CreateMessage(), CreateMessage() };
        foreach (var message in messages)
        {
            await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);
        }

        var messageIds = messages.Select(m => m.Id).ToArray();
        await repository.MarkAsCompletedAsync(messageIds, cancellationToken).ConfigureAwait(false);

        var statuses = await GetStatusesAsync(messageIds, cancellationToken).ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(statuses.Count).IsEqualTo(3);
            _ = await Assert.That(statuses).All(s => s == (long)OutboxMessageStatus.Completed);
        }
    }

    [Test]
    public async Task MarkAsFailedAsync_WithMultipleMessageIds_MarksAllAsFailed(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IOutboxRepository repository = CreateRepository();
        var messages = new[] { CreateMessage(), CreateMessage() };
        foreach (var message in messages)
        {
            await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);
        }

        var messageIds = messages.Select(m => m.Id).ToArray();
        await repository.MarkAsFailedAsync(messageIds, "Batch error", cancellationToken).ConfigureAwait(false);

        foreach (var messageId in messageIds)
        {
            var cmd = new SqliteCommand(
                "SELECT \"Status\", \"Error\", \"RetryCount\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
                _keepAlive
            );
            await using (cmd.ConfigureAwait(false))
            {
                _ = cmd.Parameters.AddWithValue("@Id", messageId.ToString());
                var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    _ = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(reader.GetInt64(0)).IsEqualTo((long)OutboxMessageStatus.Failed);
                        _ = await Assert.That(reader.GetString(1)).IsEqualTo("Batch error");
                        _ = await Assert.That(reader.GetInt64(2)).IsEqualTo(1L);
                    }
                }
            }
        }
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_WithMultipleMessageIds_MarksAllAsDeadLetter(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IOutboxRepository repository = CreateRepository();
        var messages = new[] { CreateMessage(), CreateMessage() };
        foreach (var message in messages)
        {
            await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);
        }

        var messageIds = messages.Select(m => m.Id).ToArray();
        await repository
            .MarkAsDeadLetterAsync(messageIds, "Fatal batch error", cancellationToken)
            .ConfigureAwait(false);

        var statuses = await GetStatusesAsync(messageIds, cancellationToken).ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(statuses.Count).IsEqualTo(2);
            _ = await Assert.That(statuses).All(s => s == (long)OutboxMessageStatus.DeadLetter);
        }
    }

    [Test]
    public async Task MarkAsCompletedAsync_WithEmptyMessageIds_DoesNotThrow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IOutboxRepository repository = CreateRepository();
        var message = CreateMessage();
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        await repository.MarkAsCompletedAsync(Array.Empty<Guid>(), cancellationToken).ConfigureAwait(false);

        var statuses = await GetStatusesAsync([message.Id], cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(statuses[0]).IsEqualTo((long)OutboxMessageStatus.Pending);
    }

    private async Task<List<long>> GetStatusesAsync(
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var statuses = new List<long>();
        foreach (var messageId in messageIds)
        {
            var cmd = new SqliteCommand("SELECT \"Status\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id", _keepAlive);
            await using (cmd.ConfigureAwait(false))
            {
                _ = cmd.Parameters.AddWithValue("@Id", messageId.ToString());
                statuses.Add((long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            }
        }

        return statuses;
    }

    private static async Task<string?> SetJournalModeDeleteAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new SqliteConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new SqliteCommand("PRAGMA journal_mode=DELETE;", connection);
            await using (command.ConfigureAwait(false))
            {
                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result as string;
            }
        }
    }

    private static async Task<string?> GetJournalModeAsync(string connectionString, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new SqliteConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new SqliteCommand("PRAGMA journal_mode;", connection);
            await using (command.ConfigureAwait(false))
            {
                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result as string;
            }
        }
    }

    private sealed class StubTransactionScope(SqliteTransaction transaction) : IOutboxTransactionScope
    {
        public object? GetCurrentTransaction() => transaction;
    }

    private sealed record TestSQLiteRepoEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
