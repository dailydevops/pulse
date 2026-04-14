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
/// Database-focused tests for <see cref="SQLiteOutboxManagement"/> using an in-memory SQLite database.
/// </summary>
[TestGroup("SQLite")]
public sealed class SQLiteOutboxManagementDatabaseTests : IAsyncDisposable
{
    private readonly string _dbIdentifier = $"mgmt_{Guid.NewGuid():N}";
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public SQLiteOutboxManagementDatabaseTests()
    {
        _connectionString = $"Data Source={_dbIdentifier};Mode=Memory;Cache=Shared";
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

    private SQLiteOutboxManagement CreateManagement(bool enableWal = false) =>
        new(
            Options.Create(new OutboxOptions { ConnectionString = _connectionString, EnableWalMode = enableWal }),
            _timeProvider
        );

    private static OutboxMessage CreateMessage(
        OutboxMessageStatus status,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? processedAt = null,
        DateTimeOffset? nextRetryAt = null,
        string? error = null,
        int retryCount = 0
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestSQLiteEvent),
            Payload = "{}",
            CorrelationId = "corr-id",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ProcessedAt = processedAt,
            NextRetryAt = nextRetryAt,
            RetryCount = retryCount,
            Error = error,
            Status = status,
        };

    private async Task InsertAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var cmd = new SqliteCommand(
            """
            INSERT INTO "OutboxMessage"
            ("Id","EventType","Payload","CorrelationId","CreatedAt","UpdatedAt","ProcessedAt","NextRetryAt","RetryCount","Error","Status")
            VALUES
            (@Id,@EventType,@Payload,@CorrelationId,@CreatedAt,@UpdatedAt,@ProcessedAt,@NextRetryAt,@RetryCount,@Error,@Status);
            """,
            _keepAlive
        );
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
            _ = cmd.Parameters.AddWithValue("@EventType", message.EventType.ToOutboxEventTypeName());
            _ = cmd.Parameters.AddWithValue("@Payload", message.Payload);
            _ = cmd.Parameters.AddWithValue("@CorrelationId", (object?)message.CorrelationId ?? DBNull.Value);
            _ = cmd.Parameters.AddWithValue("@CreatedAt", message.CreatedAt);
            _ = cmd.Parameters.AddWithValue("@UpdatedAt", message.UpdatedAt);
            _ = cmd.Parameters.AddWithValue(
                "@ProcessedAt",
                message.ProcessedAt.HasValue ? (object)message.ProcessedAt.Value : DBNull.Value
            );
            _ = cmd.Parameters.AddWithValue(
                "@NextRetryAt",
                message.NextRetryAt.HasValue ? (object)message.NextRetryAt.Value : DBNull.Value
            );
            _ = cmd.Parameters.AddWithValue("@RetryCount", message.RetryCount);
            _ = cmd.Parameters.AddWithValue("@Error", (object?)message.Error ?? DBNull.Value);
            _ = cmd.Parameters.AddWithValue("@Status", (int)message.Status);

            _ = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GetDeadLetterCountAsync_ReturnsExpectedCount(CancellationToken cancellationToken)
    {
        var management = CreateManagement();
        await InsertAsync(CreateMessage(OutboxMessageStatus.DeadLetter), cancellationToken).ConfigureAwait(false);
        await InsertAsync(CreateMessage(OutboxMessageStatus.DeadLetter), cancellationToken).ConfigureAwait(false);

        var count = await management.GetDeadLetterCountAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(2L);
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_ReturnsPagedOrdered(CancellationToken cancellationToken)
    {
        var management = CreateManagement();
        var older = CreateMessage(OutboxMessageStatus.DeadLetter, createdAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = CreateMessage(OutboxMessageStatus.DeadLetter, createdAt: DateTimeOffset.UtcNow);
        await InsertAsync(older, cancellationToken).ConfigureAwait(false);
        await InsertAsync(newer, cancellationToken).ConfigureAwait(false);

        var messages = await management
            .GetDeadLetterMessagesAsync(pageSize: 1, page: 0, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(messages.Count).IsEqualTo(1);
            _ = await Assert.That(messages[0].Id).IsEqualTo(newer.Id);
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_ReturnsSingleMessage(CancellationToken cancellationToken)
    {
        var management = CreateManagement();
        var target = CreateMessage(OutboxMessageStatus.DeadLetter);
        await InsertAsync(target, cancellationToken).ConfigureAwait(false);

        var message = await management.GetDeadLetterMessageAsync(target.Id, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(message!.Id).IsEqualTo(target.Id);
    }

    [Test]
    public async Task ReplayMessageAsync_ResetsDeadLetterFields(CancellationToken cancellationToken)
    {
        var management = CreateManagement(enableWal: true);
        var deadLetter = CreateMessage(
            OutboxMessageStatus.DeadLetter,
            processedAt: DateTimeOffset.UtcNow,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(1),
            error: "fatal",
            retryCount: 3
        );
        await InsertAsync(deadLetter, cancellationToken).ConfigureAwait(false);

        var result = await management.ReplayMessageAsync(deadLetter.Id, cancellationToken).ConfigureAwait(false);

        var cmd = new SqliteCommand(
            "SELECT \"Status\",\"Error\",\"ProcessedAt\",\"NextRetryAt\",\"RetryCount\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            _keepAlive
        );
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@Id", deadLetter.Id.ToString());
            var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                _ = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                using (Assert.Multiple())
                {
                    _ = await Assert.That(result).IsTrue();
                    _ = await Assert.That(reader.GetInt64(0)).IsEqualTo((long)OutboxMessageStatus.Pending);
                    _ = await Assert
                        .That(await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false))
                        .IsTrue();
                    _ = await Assert
                        .That(await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false))
                        .IsTrue();
                    _ = await Assert
                        .That(await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false))
                        .IsTrue();
                    _ = await Assert.That(reader.GetInt64(4)).IsEqualTo(0);
                }
            }
        }
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_ResetsAllMessages(CancellationToken cancellationToken)
    {
        var management = CreateManagement();
        await InsertAsync(CreateMessage(OutboxMessageStatus.DeadLetter), cancellationToken).ConfigureAwait(false);
        await InsertAsync(CreateMessage(OutboxMessageStatus.DeadLetter), cancellationToken).ConfigureAwait(false);

        var updated = await management.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

        var cmd = new SqliteCommand("SELECT COUNT(*) FROM \"OutboxMessage\" WHERE \"Status\" = @status", _keepAlive);
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@status", (int)OutboxMessageStatus.Pending);
            var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

            using (Assert.Multiple())
            {
                _ = await Assert.That(updated).IsGreaterThanOrEqualTo(2);
                _ = await Assert.That(count).IsGreaterThanOrEqualTo(2L);
            }
        }
    }

    [Test]
    public async Task GetStatisticsAsync_ReturnsAggregatedCounts(CancellationToken cancellationToken)
    {
        var management = CreateManagement();
        await InsertAsync(CreateMessage(OutboxMessageStatus.Pending), cancellationToken).ConfigureAwait(false);
        await InsertAsync(CreateMessage(OutboxMessageStatus.Processing), cancellationToken).ConfigureAwait(false);
        await InsertAsync(CreateMessage(OutboxMessageStatus.Completed), cancellationToken).ConfigureAwait(false);
        await InsertAsync(CreateMessage(OutboxMessageStatus.Failed), cancellationToken).ConfigureAwait(false);
        await InsertAsync(CreateMessage(OutboxMessageStatus.DeadLetter), cancellationToken).ConfigureAwait(false);

        var stats = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(stats.Pending).IsEqualTo(1);
            _ = await Assert.That(stats.Processing).IsEqualTo(1);
            _ = await Assert.That(stats.Completed).IsEqualTo(1);
            _ = await Assert.That(stats.Failed).IsEqualTo(1);
            _ = await Assert.That(stats.DeadLetter).IsEqualTo(1);
        }
    }

    private sealed record TestSQLiteEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
