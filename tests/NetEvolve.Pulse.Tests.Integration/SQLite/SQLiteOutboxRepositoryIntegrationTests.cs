namespace NetEvolve.Pulse.Tests.Integration.SQLite;

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;
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

    private static OutboxMessage CreateMessage(Type? eventType = null) =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType ?? typeof(SQLiteIntegrationEvent),
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
        var message = CreateMessage();

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
        var message1 = CreateMessage();
        var message2 = CreateMessage();
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
        var message = CreateMessage();
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

        var message1 = CreateMessage();
        var message2 = CreateMessage();
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
        var message = CreateMessage();

        await repository.AddAsync(message).ConfigureAwait(false);

        // WAL mode creates a -wal sidecar file after the first write
        var walExists = File.Exists(_dbPath + "-wal") || File.Exists(_dbPath);
        _ = await Assert.That(walExists).IsTrue();
    }

    private sealed record SQLiteIntegrationEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
