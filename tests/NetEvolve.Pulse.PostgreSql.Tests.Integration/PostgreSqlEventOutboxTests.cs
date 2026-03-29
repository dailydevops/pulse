namespace NetEvolve.Pulse.PostgreSql.Tests.Integration;

using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.PostgreSql.Tests.Integration.Fixtures;
using Npgsql;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="PostgreSqlEventOutbox"/>.
/// Tests event storage with transaction support against a real PostgreSQL database.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgreSqlEventOutboxTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlEventOutboxTests"/> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL container fixture.</param>
    public PostgreSqlEventOutboxTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"pgeventtest_{Guid.NewGuid():N}";
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
    public async Task StoreAsync_WithValidEvent_PersistsToDatabase()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new PostgreSqlEventOutbox(connection, options, TimeProvider.System);

        var @event = new TestPgOutboxEvent("store-1", "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        await using var verifyConnection = new NpgsqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM \"pulse\".\"OutboxMessage\" WHERE \"EventType\" LIKE '%TestPgOutboxEvent%'",
            verifyConnection
        );
        var count = (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(1L);
    }

    [Test]
    public async Task StoreAsync_WithEventId_UsesProvidedId()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new PostgreSqlEventOutbox(connection, options, TimeProvider.System);

        var eventId = Guid.NewGuid();
        var @event = new TestPgOutboxEvent(eventId.ToString(), "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        await using var verifyConnection = new NpgsqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\" FROM \"pulse\".\"OutboxMessage\" WHERE \"Id\" = @Id",
            verifyConnection
        );
        _ = command.Parameters.AddWithValue("Id", eventId);
        var storedId = await command.ExecuteScalarAsync().ConfigureAwait(false);

        _ = await Assert.That(storedId).IsEqualTo(eventId);
    }

    [Test]
    public async Task StoreAsync_WithCorrelationId_PersistsCorrelationId()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new PostgreSqlEventOutbox(connection, options, TimeProvider.System);

        var @event = new TestPgOutboxEvent("correlation-1", "Test data") { CorrelationId = "trace-123" };

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        await using var verifyConnection = new NpgsqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"CorrelationId\" FROM \"pulse\".\"OutboxMessage\" WHERE \"EventType\" LIKE '%TestPgOutboxEvent%'",
            verifyConnection
        );
        var correlationId = await command.ExecuteScalarAsync().ConfigureAwait(false);

        _ = await Assert.That(correlationId).IsEqualTo("trace-123");
    }

    [Test]
    public async Task StoreAsync_WithTransaction_EnlistsInTransaction()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new PostgreSqlEventOutbox(connection, options, TimeProvider.System, transaction);

        var @event = new TestPgOutboxEvent("transaction-1", "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        await transaction.RollbackAsync().ConfigureAwait(false);

        await using var verifyConnection = new NpgsqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM \"pulse\".\"OutboxMessage\" WHERE \"EventType\" LIKE '%TestPgOutboxEvent%'",
            verifyConnection
        );
        var count = (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(0L);
    }

    [Test]
    public async Task StoreAsync_WithTransactionCommit_PersistsEvent()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new PostgreSqlEventOutbox(connection, options, TimeProvider.System, transaction);

        var @event = new TestPgOutboxEvent("commit-1", "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        await transaction.CommitAsync().ConfigureAwait(false);

        await using var verifyConnection = new NpgsqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM \"pulse\".\"OutboxMessage\" WHERE \"EventType\" LIKE '%TestPgOutboxEvent%'",
            verifyConnection
        );
        var count = (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(1L);
    }

    [Test]
    public async Task StoreAsync_SetsCorrectStatus()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new PostgreSqlEventOutbox(connection, options, TimeProvider.System);

        var @event = new TestPgOutboxEvent("status-1", "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        await using var verifyConnection = new NpgsqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"Status\" FROM \"pulse\".\"OutboxMessage\" WHERE \"EventType\" LIKE '%TestPgOutboxEvent%'",
            verifyConnection
        );
        var status = (int)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(status).IsEqualTo((int)OutboxMessageStatus.Pending);
    }

    [Test]
    public async Task StoreAsync_SerializesPayloadCorrectly()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new PostgreSqlEventOutbox(connection, options, TimeProvider.System);

        var @event = new TestPgOutboxEvent("payload-1", "Test payload data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        await using var verifyConnection = new NpgsqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"Payload\" FROM \"pulse\".\"OutboxMessage\" WHERE \"EventType\" LIKE '%TestPgOutboxEvent%'",
            verifyConnection
        );
        var payload = (string)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        using (Assert.Multiple())
        {
            _ = await Assert.That(payload).Contains("payload-1");
            _ = await Assert.That(payload).Contains("Test payload data");
        }
    }

    /// <summary>
    /// Test event for PostgreSQL event outbox integration tests.
    /// </summary>
    internal sealed class TestPgOutboxEvent : IEvent
    {
        public TestPgOutboxEvent() { }

        public TestPgOutboxEvent(string id, string data)
        {
            Id = id;
            Data = data;
        }

        public string Id { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
