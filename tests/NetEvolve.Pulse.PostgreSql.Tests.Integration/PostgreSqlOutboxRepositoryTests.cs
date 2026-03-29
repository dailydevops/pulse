namespace NetEvolve.Pulse.PostgreSql.Tests.Integration;

using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.PostgreSql.Tests.Integration.Fixtures;
using Npgsql;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="PostgreSqlOutboxRepository"/>.
/// Tests repository operations against a real PostgreSQL database using Testcontainers.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgreSqlOutboxRepositoryTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL container fixture.</param>
    public PostgreSqlOutboxRepositoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"pgrepotest_{Guid.NewGuid():N}";
    }

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName).ConfigureAwait(false);
        await _fixture.InitializeSchemaAsync(_databaseName).ConfigureAwait(false);
    }

    [After(Test)]
    public async Task CleanupAsync() => await _fixture.DropDatabaseAsync(_databaseName).ConfigureAwait(false);

    [Test]
    public async Task AddAsync_WithValidMessage_PersistsToDatabase()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);

        var message = CreateMessage("add-test");

        await repository.AddAsync(message).ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM \"pulse\".\"OutboxMessage\" WHERE \"Id\" = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("Id", message.Id);
        var count = (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(1L);
    }

    [Test]
    public async Task GetPendingAsync_WithPendingMessages_ReturnsMessagesAndMarksAsProcessing()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);

        var message1 = CreateMessage("pending-1");
        var message2 = CreateMessage("pending-2");
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);

        var pending = await repository.GetPendingAsync(10).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(pending).Count().IsEqualTo(2);
            _ = await Assert.That(pending[0].Status).IsEqualTo(OutboxMessageStatus.Processing);
            _ = await Assert.That(pending[1].Status).IsEqualTo(OutboxMessageStatus.Processing);
        }
    }

    [Test]
    public async Task GetPendingAsync_WithBatchSize_RespectsLimit()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);

        for (var i = 0; i < 5; i++)
        {
            await repository.AddAsync(CreateMessage($"batch-{i}")).ConfigureAwait(false);
        }

        var pending = await repository.GetPendingAsync(2).ConfigureAwait(false);

        _ = await Assert.That(pending).Count().IsEqualTo(2);
    }

    [Test]
    public async Task MarkAsCompletedAsync_WithValidMessage_UpdatesStatusAndProcessedAt()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);

        var message = CreateMessage("complete-test");
        await repository.AddAsync(message).ConfigureAwait(false);

        _ = await repository.GetPendingAsync(1).ConfigureAwait(false);

        await repository.MarkAsCompletedAsync(message.Id).ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"Status\", \"ProcessedAt\" FROM \"pulse\".\"OutboxMessage\" WHERE \"Id\" = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("Id", message.Id);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        _ = await reader.ReadAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reader.GetInt32(0)).IsEqualTo((int)OutboxMessageStatus.Completed);
            _ = await Assert.That(await reader.IsDBNullAsync(1).ConfigureAwait(false)).IsFalse();
        }
    }

    [Test]
    public async Task MarkAsFailedAsync_WithValidMessage_UpdatesStatusAndRetryCount()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);

        var message = CreateMessage("fail-test");
        await repository.AddAsync(message).ConfigureAwait(false);

        _ = await repository.GetPendingAsync(1).ConfigureAwait(false);

        await repository.MarkAsFailedAsync(message.Id, "Test error message").ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"Status\", \"RetryCount\", \"Error\" FROM \"pulse\".\"OutboxMessage\" WHERE \"Id\" = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("Id", message.Id);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        _ = await reader.ReadAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reader.GetInt32(0)).IsEqualTo((int)OutboxMessageStatus.Failed);
            _ = await Assert.That(reader.GetInt32(1)).IsEqualTo(1);
            _ = await Assert.That(reader.GetString(2)).IsEqualTo("Test error message");
        }
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_WithValidMessage_UpdatesStatus()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);

        var message = CreateMessage("dead-letter-test");
        await repository.AddAsync(message).ConfigureAwait(false);

        _ = await repository.GetPendingAsync(1).ConfigureAwait(false);

        await repository.MarkAsDeadLetterAsync(message.Id, "Max retries exceeded").ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"Status\", \"Error\" FROM \"pulse\".\"OutboxMessage\" WHERE \"Id\" = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("Id", message.Id);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        _ = await reader.ReadAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reader.GetInt32(0)).IsEqualTo((int)OutboxMessageStatus.DeadLetter);
            _ = await Assert.That(reader.GetString(1)).IsEqualTo("Max retries exceeded");
        }
    }

    [Test]
    public async Task GetFailedForRetryAsync_WithFailedMessages_ReturnsEligibleMessages()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);

        var message1 = CreateMessage("retry-eligible");
        var message2 = CreateMessage("retry-exceeded");
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);

        _ = await repository.GetPendingAsync(2).ConfigureAwait(false);
        await repository.MarkAsFailedAsync(message1.Id, "Error 1").ConfigureAwait(false);
        await repository.MarkAsFailedAsync(message2.Id, "Error 2").ConfigureAwait(false);

        // Set message2 retry count high to exceed max
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var updateCommand = new NpgsqlCommand(
            "UPDATE \"pulse\".\"OutboxMessage\" SET \"RetryCount\" = 5 WHERE \"Id\" = @Id",
            connection
        );
        _ = updateCommand.Parameters.AddWithValue("Id", message2.Id);
        _ = await updateCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

        var forRetry = await repository.GetFailedForRetryAsync(3, 10).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(forRetry).Count().IsEqualTo(1);
            _ = await Assert.That(forRetry[0].Id).IsEqualTo(message1.Id);
        }
    }

    [Test]
    public async Task DeleteCompletedAsync_WithOldCompletedMessages_DeletesThem()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);

        var message1 = CreateMessage("old-completed");
        var message2 = CreateMessage("recent-completed");
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);

        _ = await repository.GetPendingAsync(2).ConfigureAwait(false);
        await repository.MarkAsCompletedAsync(message1.Id).ConfigureAwait(false);
        await repository.MarkAsCompletedAsync(message2.Id).ConfigureAwait(false);

        // Backdate message1 to 10 days ago
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var updateCmd = new NpgsqlCommand(
            "UPDATE \"pulse\".\"OutboxMessage\" SET \"ProcessedAt\" = @ProcessedAt WHERE \"Id\" = @Id",
            connection
        );
        _ = updateCmd.Parameters.AddWithValue("Id", message1.Id);
        _ = updateCmd.Parameters.AddWithValue("ProcessedAt", DateTimeOffset.UtcNow.AddDays(-10));
        _ = await updateCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

        var deletedCount = await repository.DeleteCompletedAsync(TimeSpan.FromDays(7)).ConfigureAwait(false);

        await using var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"pulse\".\"OutboxMessage\"", connection);
        var remaining = (long)(await countCmd.ExecuteScalarAsync().ConfigureAwait(false))!;

        using (Assert.Multiple())
        {
            _ = await Assert.That(deletedCount).IsEqualTo(1);
            _ = await Assert.That(remaining).IsEqualTo(1L);
        }
    }

    [Test]
    public async Task ConcurrentGetPending_WithMultipleConsumers_DoesNotReturnSameMessages()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository1 = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);
        var repository2 = new PostgreSqlOutboxRepository(connectionString, options, TimeProvider.System);

        for (var i = 0; i < 10; i++)
        {
            await repository1.AddAsync(CreateMessage($"concurrent-{i}")).ConfigureAwait(false);
        }

        // Act - Simulate concurrent consumers
        var task1 = repository1.GetPendingAsync(5);
        var task2 = repository2.GetPendingAsync(5);

        var results = await Task.WhenAll(task1, task2).ConfigureAwait(false);
        var allMessages = results.SelectMany(r => r).ToList();

        // Assert - No duplicates should exist (FOR UPDATE SKIP LOCKED)
        var distinctIds = allMessages.Select(m => m.Id).Distinct().ToList();
        _ = await Assert.That(distinctIds).Count().IsEqualTo(allMessages.Count);
    }

    private static OutboxMessage CreateMessage(string id)
    {
        var now = DateTimeOffset.UtcNow;
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = $"TestEvent.{id}",
            Payload = $"{{\"Id\":\"{id}\"}}",
            CorrelationId = $"corr-{id}",
            CreatedAt = now,
            UpdatedAt = now,
            Status = OutboxMessageStatus.Pending,
        };
    }
}
