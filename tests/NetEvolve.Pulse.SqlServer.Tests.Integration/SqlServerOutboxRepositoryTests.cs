namespace NetEvolve.Pulse.SqlServer.Tests.Integration;

using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.SqlServer.Tests.Integration.Fixtures;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="SqlServerOutboxRepository"/>.
/// Tests repository operations against a real SQL Server database using Testcontainers.
/// </summary>
[ClassDataSource<SqlServerContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class SqlServerOutboxRepositoryTests
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerOutboxRepositoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The SQL Server container fixture.</param>
    public SqlServerOutboxRepositoryTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"SqlRepoTests_{Guid.NewGuid():N}";
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
        // Arrange
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);

        var message = CreateMessage("add-test");

        // Act
        await repository.AddAsync(message).ConfigureAwait(false);

        // Assert
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM [pulse].[OutboxMessage] WHERE [Id] = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("@Id", message.Id);
        var count = (int)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task GetPendingAsync_WithPendingMessages_ReturnsMessagesAndMarksAsProcessing()
    {
        // Arrange
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);

        var message1 = CreateMessage("pending-1");
        var message2 = CreateMessage("pending-2");
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);

        // Act
        var pending = await repository.GetPendingAsync(10).ConfigureAwait(false);

        // Assert
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
        // Arrange
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);

        for (var i = 0; i < 5; i++)
        {
            await repository.AddAsync(CreateMessage($"batch-{i}")).ConfigureAwait(false);
        }

        // Act
        var pending = await repository.GetPendingAsync(2).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(pending).Count().IsEqualTo(2);
    }

    [Test]
    public async Task MarkAsCompletedAsync_WithValidMessage_UpdatesStatusAndProcessedAt()
    {
        // Arrange
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);

        var message = CreateMessage("complete-test");
        await repository.AddAsync(message).ConfigureAwait(false);

        // First get it to mark as processing
        _ = await repository.GetPendingAsync(1).ConfigureAwait(false);

        // Act
        await repository.MarkAsCompletedAsync(message.Id).ConfigureAwait(false);

        // Assert
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT [Status], [ProcessedAt] FROM [pulse].[OutboxMessage] WHERE [Id] = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("@Id", message.Id);
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
        // Arrange
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);

        var message = CreateMessage("fail-test");
        await repository.AddAsync(message).ConfigureAwait(false);

        // First get it to mark as processing
        _ = await repository.GetPendingAsync(1).ConfigureAwait(false);

        // Act
        await repository.MarkAsFailedAsync(message.Id, "Test error message").ConfigureAwait(false);

        // Assert
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT [Status], [RetryCount], [Error] FROM [pulse].[OutboxMessage] WHERE [Id] = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("@Id", message.Id);
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
        // Arrange
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);

        var message = CreateMessage("dead-letter-test");
        await repository.AddAsync(message).ConfigureAwait(false);

        // First get it to mark as processing
        _ = await repository.GetPendingAsync(1).ConfigureAwait(false);

        // Act
        await repository.MarkAsDeadLetterAsync(message.Id, "Max retries exceeded").ConfigureAwait(false);

        // Assert
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT [Status], [Error] FROM [pulse].[OutboxMessage] WHERE [Id] = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("@Id", message.Id);
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
        // Arrange
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);

        var message1 = CreateMessage("retry-eligible");
        var message2 = CreateMessage("retry-exceeded");
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);

        // Mark as failed with different retry counts
        _ = await repository.GetPendingAsync(2).ConfigureAwait(false);
        await repository.MarkAsFailedAsync(message1.Id, "Error 1").ConfigureAwait(false); // RetryCount = 1
        await repository.MarkAsFailedAsync(message2.Id, "Error 2").ConfigureAwait(false); // RetryCount = 1

        // Add more retries to message2 to exceed max
        for (var i = 0; i < 4; i++)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new SqlCommand(
                "UPDATE [pulse].[OutboxMessage] SET [RetryCount] = 5 WHERE [Id] = @Id",
                connection
            );
            _ = command.Parameters.AddWithValue("@Id", message2.Id);
            _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // Act
        var forRetry = await repository.GetFailedForRetryAsync(3, 10).ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(forRetry).Count().IsEqualTo(1);
            _ = await Assert.That(forRetry[0].Id).IsEqualTo(message1.Id);
        }
    }

    [Test]
    public async Task DeleteCompletedAsync_WithOldCompletedMessages_DeletesThem()
    {
        // Arrange
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);

        var message1 = CreateMessage("old-completed");
        var message2 = CreateMessage("recent-completed");
        await repository.AddAsync(message1).ConfigureAwait(false);
        await repository.AddAsync(message2).ConfigureAwait(false);

        // Get pending and complete both
        _ = await repository.GetPendingAsync(2).ConfigureAwait(false);
        await repository.MarkAsCompletedAsync(message1.Id).ConfigureAwait(false);
        await repository.MarkAsCompletedAsync(message2.Id).ConfigureAwait(false);

        // Backdate message1 to 10 days ago
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var updateCmd = new SqlCommand(
            "UPDATE [pulse].[OutboxMessage] SET [ProcessedAt] = @ProcessedAt WHERE [Id] = @Id",
            connection
        );
        _ = updateCmd.Parameters.AddWithValue("@Id", message1.Id);
        _ = updateCmd.Parameters.AddWithValue("@ProcessedAt", DateTimeOffset.UtcNow.AddDays(-10));
        _ = await updateCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

        // Act
        var deletedCount = await repository.DeleteCompletedAsync(TimeSpan.FromDays(7)).ConfigureAwait(false);

        // Assert
        await using var countCmd = new SqlCommand("SELECT COUNT(*) FROM [pulse].[OutboxMessage]", connection);
        var remaining = (int)(await countCmd.ExecuteScalarAsync().ConfigureAwait(false))!;

        using (Assert.Multiple())
        {
            _ = await Assert.That(deletedCount).IsEqualTo(1);
            _ = await Assert.That(remaining).IsEqualTo(1);
        }
    }

    [Test]
    [Retry(5)] // Deadlocks can occur with concurrent SQL Server transactions - more retries needed
    public async Task ConcurrentGetPending_WithMultipleConsumers_DoesNotReturnSameMessages()
    {
        // Arrange
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var options = Options.Create(new OutboxOptions());
        var repository1 = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);
        var repository2 = new SqlServerOutboxRepository(connectionString, options, TimeProvider.System);

        for (var i = 0; i < 10; i++)
        {
            await repository1.AddAsync(CreateMessage($"concurrent-{i}")).ConfigureAwait(false);
        }

        // Act - Simulate concurrent consumers with staggered start to reduce deadlock probability
        var task1 = repository1.GetPendingAsync(5);
        await Task.Delay(50).ConfigureAwait(false); // Small delay to reduce contention
        var task2 = repository2.GetPendingAsync(5);

        var results = await Task.WhenAll(task1, task2).ConfigureAwait(false);
        var allMessages = results.SelectMany(r => r).ToList();

        // Assert - No duplicates should exist
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
