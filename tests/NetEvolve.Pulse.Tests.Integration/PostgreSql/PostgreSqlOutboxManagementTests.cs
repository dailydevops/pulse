namespace NetEvolve.Pulse.Tests.Integration.PostgreSql;

using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;
using Npgsql;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="PostgreSqlOutboxManagement"/>.
/// Tests management operations against a real PostgreSQL database using Testcontainers.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgreSqlOutboxManagementTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxManagementTests"/> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL container fixture.</param>
    public PostgreSqlOutboxManagementTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"pgmgmttest_{Guid.NewGuid():N}";
    }

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName).ConfigureAwait(false);
        await _fixture.InitializeSchemaAsync(_databaseName).ConfigureAwait(false);
    }

    [After(Test)]
    public async Task CleanupAsync() => await _fixture.DropDatabaseAsync(_databaseName).ConfigureAwait(false);

    // -------------------------------------------------------------------------
    // GetDeadLetterMessagesAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetDeadLetterMessagesAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var result = await management.GetDeadLetterMessagesAsync().ConfigureAwait(false);

        _ = await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithDeadLetterMessages_ReturnsPagedResults()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.Pending).ConfigureAwait(false);

        var result = await management.GetDeadLetterMessagesAsync(pageSize: 10, page: 0).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).Count().IsEqualTo(2);
            _ = await Assert.That(result[0].Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
            _ = await Assert.That(result[1].Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithPageSize_RespectsLimit()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        for (var i = 0; i < 5; i++)
        {
            _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        }

        var result = await management.GetDeadLetterMessagesAsync(pageSize: 2, page: 0).ConfigureAwait(false);

        _ = await Assert.That(result).Count().IsEqualTo(2);
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithPage_SkipsCorrectly()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        for (var i = 0; i < 4; i++)
        {
            _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        }

        var page0 = await management.GetDeadLetterMessagesAsync(pageSize: 2, page: 0).ConfigureAwait(false);
        var page1 = await management.GetDeadLetterMessagesAsync(pageSize: 2, page: 1).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(page0).Count().IsEqualTo(2);
            _ = await Assert.That(page1).Count().IsEqualTo(2);
            _ = await Assert.That(page0[0].Id).IsNotEqualTo(page1[0].Id);
            _ = await Assert.That(page0[1].Id).IsNotEqualTo(page1[1].Id);
        }
    }

    // -------------------------------------------------------------------------
    // GetDeadLetterMessageAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetDeadLetterMessageAsync_WithExistingDeadLetterMessage_ReturnsMessage()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var id = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);

        var result = await management.GetDeadLetterMessageAsync(id).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsNotNull();
            _ = await Assert.That(result!.Id).IsEqualTo(id);
            _ = await Assert.That(result.Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithNonDeadLetterMessage_ReturnsNull()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var id = await SeedMessageAsync(connectionString, OutboxMessageStatus.Pending).ConfigureAwait(false);

        var result = await management.GetDeadLetterMessageAsync(id).ConfigureAwait(false);

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WithUnknownId_ReturnsNull()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var result = await management.GetDeadLetterMessageAsync(Guid.NewGuid()).ConfigureAwait(false);

        _ = await Assert.That(result).IsNull();
    }

    // -------------------------------------------------------------------------
    // GetDeadLetterCountAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetDeadLetterCountAsync_EmptyDatabase_ReturnsZero()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var count = await management.GetDeadLetterCountAsync().ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(0L);
    }

    [Test]
    public async Task GetDeadLetterCountAsync_WithDeadLetterMessages_ReturnsCorrectCount()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.Pending).ConfigureAwait(false);

        var count = await management.GetDeadLetterCountAsync().ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(2L);
    }

    // -------------------------------------------------------------------------
    // ReplayMessageAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task ReplayMessageAsync_WithExistingDeadLetterMessage_ReturnsTrueAndResetsMessage()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var id = await SeedMessageAsync(
                connectionString,
                OutboxMessageStatus.DeadLetter,
                retryCount: 3,
                error: "Max retries exceeded"
            )
            .ConfigureAwait(false);

        var replayed = await management.ReplayMessageAsync(id).ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"Status\", \"RetryCount\", \"Error\" FROM \"pulse\".\"OutboxMessage\" WHERE \"Id\" = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("Id", id);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        _ = await reader.ReadAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(replayed).IsTrue();
            _ = await Assert.That(reader.GetInt32(0)).IsEqualTo((int)OutboxMessageStatus.Pending);
            _ = await Assert.That(reader.GetInt32(1)).IsEqualTo(0);
            _ = await Assert.That(await reader.IsDBNullAsync(2).ConfigureAwait(false)).IsTrue();
        }
    }

    [Test]
    public async Task ReplayMessageAsync_WithNonDeadLetterMessage_ReturnsFalse()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var id = await SeedMessageAsync(connectionString, OutboxMessageStatus.Pending).ConfigureAwait(false);

        var replayed = await management.ReplayMessageAsync(id).ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"Status\" FROM \"pulse\".\"OutboxMessage\" WHERE \"Id\" = @Id",
            connection
        );
        _ = command.Parameters.AddWithValue("Id", id);
        var status = (int)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        using (Assert.Multiple())
        {
            _ = await Assert.That(replayed).IsFalse();
            _ = await Assert.That(status).IsEqualTo((int)OutboxMessageStatus.Pending);
        }
    }

    [Test]
    public async Task ReplayMessageAsync_WithUnknownId_ReturnsFalse()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var replayed = await management.ReplayMessageAsync(Guid.NewGuid()).ConfigureAwait(false);

        _ = await Assert.That(replayed).IsFalse();
    }

    // -------------------------------------------------------------------------
    // ReplayAllDeadLetterAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task ReplayAllDeadLetterAsync_EmptyDatabase_ReturnsZero()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var count = await management.ReplayAllDeadLetterAsync().ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_WithDeadLetterMessages_ResetsAllAndReturnsCount()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.Pending).ConfigureAwait(false);

        var count = await management.ReplayAllDeadLetterAsync().ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM \"pulse\".\"OutboxMessage\" WHERE \"Status\" = @Status",
            connection
        );
        _ = command.Parameters.AddWithValue("Status", (int)OutboxMessageStatus.DeadLetter);
        var remaining = (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        using (Assert.Multiple())
        {
            _ = await Assert.That(count).IsEqualTo(3);
            _ = await Assert.That(remaining).IsEqualTo(0L);
        }
    }

    // -------------------------------------------------------------------------
    // GetStatisticsAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetStatisticsAsync_EmptyDatabase_ReturnsZeroStatistics()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        var stats = await management.GetStatisticsAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(stats.Pending).IsEqualTo(0L);
            _ = await Assert.That(stats.Processing).IsEqualTo(0L);
            _ = await Assert.That(stats.Completed).IsEqualTo(0L);
            _ = await Assert.That(stats.Failed).IsEqualTo(0L);
            _ = await Assert.That(stats.DeadLetter).IsEqualTo(0L);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_WithMessages_ReturnsCorrectCounts()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        var management = new PostgreSqlOutboxManagement(connectionString, Options.Create(new OutboxOptions()));

        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.Pending).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.Pending).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.Processing).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.Completed).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.Failed).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);
        _ = await SeedMessageAsync(connectionString, OutboxMessageStatus.DeadLetter).ConfigureAwait(false);

        var stats = await management.GetStatisticsAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(stats.Pending).IsEqualTo(2L);
            _ = await Assert.That(stats.Processing).IsEqualTo(1L);
            _ = await Assert.That(stats.Completed).IsEqualTo(1L);
            _ = await Assert.That(stats.Failed).IsEqualTo(1L);
            _ = await Assert.That(stats.DeadLetter).IsEqualTo(2L);
        }
    }

    // -------------------------------------------------------------------------
    // Seed helper
    // -------------------------------------------------------------------------

    private static async Task<Guid> SeedMessageAsync(
        string connectionString,
        OutboxMessageStatus status,
        string? eventType = null,
        int retryCount = 0,
        string? error = null
    )
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var resolvedEventType = eventType ?? typeof(TestPgManagementEvent).AssemblyQualifiedName!;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO "pulse"."OutboxMessage"
                ("Id", "EventType", "Payload", "CorrelationId", "CreatedAt", "UpdatedAt", "RetryCount", "Error", "Status")
            VALUES
                (@Id, @EventType, @Payload, NULL, @CreatedAt, @UpdatedAt, @RetryCount, @Error, @Status)
            """,
            connection
        );

        _ = command.Parameters.AddWithValue("Id", id);
        _ = command.Parameters.AddWithValue("EventType", resolvedEventType);
        _ = command.Parameters.AddWithValue("Payload", $"{{\"type\":\"{resolvedEventType}\"}}");
        _ = command.Parameters.AddWithValue("CreatedAt", now);
        _ = command.Parameters.AddWithValue("UpdatedAt", now);
        _ = command.Parameters.AddWithValue("RetryCount", retryCount);
        _ = command.Parameters.AddWithValue("Error", (object?)error ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("Status", (int)status);

        _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        return id;
    }

    private sealed record TestPgManagementEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
