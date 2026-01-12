namespace NetEvolve.Pulse.SqlServer.Tests.Integration;

using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.SqlServer.Tests.Integration.Fixtures;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="SqlServerEventOutbox"/>.
/// Tests event storage with transaction support against a real SQL Server database.
/// </summary>
[ClassDataSource<SqlServerContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class SqlServerEventOutboxTests
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerEventOutboxTests"/> class.
    /// </summary>
    /// <param name="fixture">The SQL Server container fixture.</param>
    public SqlServerEventOutboxTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"SqlEventOutboxTests_{Guid.NewGuid():N}";
    }

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName).ConfigureAwait(false);
        await _fixture.InitializeSchemaAsync(_databaseName).ConfigureAwait(false);
    }

    [After(Test)]
    public async Task CleanupAsync() => await _fixture.DropDatabaseAsync(_databaseName).ConfigureAwait(false);

    #region Constructor Tests

    [Test]
    public async Task Constructor_WithNullConnection_ThrowsArgumentNullException()
    {
        SqlConnection? connection = null;
        var options = Options.Create(new OutboxOptions());

        _ = Assert.Throws<ArgumentNullException>(
            "connection",
            () => _ = new SqlServerEventOutbox(connection!, options, TimeProvider.System)
        );
    }

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        await using var connection = new SqlConnection(_fixture.GetConnectionString(_databaseName));
        IOptions<OutboxOptions>? options = null;

        _ = Assert.Throws<ArgumentNullException>(
            "options",
            () => _ = new SqlServerEventOutbox(connection, options!, TimeProvider.System)
        );
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        await using var connection = new SqlConnection(_fixture.GetConnectionString(_databaseName));
        var options = Options.Create(new OutboxOptions());
        TimeProvider? timeProvider = null;

        _ = Assert.Throws<ArgumentNullException>(
            "timeProvider",
            () => _ = new SqlServerEventOutbox(connection, options, timeProvider!)
        );
    }

    [Test]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        await using var connection = new SqlConnection(_fixture.GetConnectionString(_databaseName));
        var options = Options.Create(new OutboxOptions());

        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System);

        _ = await Assert.That(outbox).IsNotNull();
    }

    #endregion

    #region StoreAsync Tests

    [Test]
    public async Task StoreAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        await using var connection = new SqlConnection(_fixture.GetConnectionString(_databaseName));
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => outbox.StoreAsync<TestSqlOutboxEvent>(null!));
    }

    [Test]
    public async Task StoreAsync_WithValidEvent_PersistsToDatabase()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System);

        var @event = new TestSqlOutboxEvent("store-1", "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        // Verify stored in database
        await using var verifyConnection = new SqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM [pulse].[OutboxMessage] WHERE [EventType] LIKE '%TestSqlOutboxEvent%'",
            verifyConnection
        );
        var count = (int)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task StoreAsync_WithEventId_UsesProvidedId()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System);

        var eventId = Guid.NewGuid();
        var @event = new TestSqlOutboxEvent(eventId.ToString(), "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        // Verify stored with correct ID
        await using var verifyConnection = new SqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT [Id] FROM [pulse].[OutboxMessage] WHERE [Id] = @Id",
            verifyConnection
        );
        _ = command.Parameters.AddWithValue("@Id", eventId);
        var storedId = await command.ExecuteScalarAsync().ConfigureAwait(false);

        _ = await Assert.That(storedId).IsEqualTo(eventId);
    }

    [Test]
    public async Task StoreAsync_WithCorrelationId_PersistsCorrelationId()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System);

        var @event = new TestSqlOutboxEvent("correlation-1", "Test data") { CorrelationId = "trace-123" };

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        // Verify correlation ID stored
        await using var verifyConnection = new SqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT [CorrelationId] FROM [pulse].[OutboxMessage] WHERE [EventType] LIKE '%TestSqlOutboxEvent%'",
            verifyConnection
        );
        var correlationId = await command.ExecuteScalarAsync().ConfigureAwait(false);

        _ = await Assert.That(correlationId).IsEqualTo("trace-123");
    }

    [Test]
    public async Task StoreAsync_WithTransaction_EnlistsInTransaction()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System, transaction);

        var @event = new TestSqlOutboxEvent("transaction-1", "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        // Rollback the transaction
        await transaction.RollbackAsync().ConfigureAwait(false);

        // Verify NOT stored (rolled back)
        await using var verifyConnection = new SqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM [pulse].[OutboxMessage] WHERE [EventType] LIKE '%TestSqlOutboxEvent%'",
            verifyConnection
        );
        var count = (int)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task StoreAsync_WithTransactionCommit_PersistsEvent()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System, transaction);

        var @event = new TestSqlOutboxEvent("commit-1", "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        // Commit the transaction
        await transaction.CommitAsync().ConfigureAwait(false);

        // Verify stored (committed)
        await using var verifyConnection = new SqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM [pulse].[OutboxMessage] WHERE [EventType] LIKE '%TestSqlOutboxEvent%'",
            verifyConnection
        );
        var count = (int)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task StoreAsync_WithCustomSchema_UsesConfiguredSchema()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);

        // Create custom schema and table
        await using (var setupConnection = new SqlConnection(connectionString))
        {
            await setupConnection.OpenAsync().ConfigureAwait(false);
            await using var schemaCmd = new SqlCommand(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'custom')
                    EXEC('CREATE SCHEMA [custom]');
                """,
                setupConnection
            );
            _ = await schemaCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            // Copy table structure to custom schema
            await using var tableCmd = new SqlCommand(
                """
                SELECT * INTO [custom].[CustomOutbox] FROM [pulse].[OutboxMessage] WHERE 1=0;
                """,
                setupConnection
            );
            _ = await tableCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions { Schema = "custom", TableName = "CustomOutbox" });
        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System);

        var @event = new TestSqlOutboxEvent("custom-schema-1", "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        // Verify stored in custom schema
        await using var verifyConnection = new SqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM [custom].[CustomOutbox] WHERE [EventType] LIKE '%TestSqlOutboxEvent%'",
            verifyConnection
        );
        var count = (int)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task StoreAsync_SetsCorrectStatus()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System);

        var @event = new TestSqlOutboxEvent("status-1", "Test data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        // Verify status is Pending (0)
        await using var verifyConnection = new SqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT [Status] FROM [pulse].[OutboxMessage] WHERE [EventType] LIKE '%TestSqlOutboxEvent%'",
            verifyConnection
        );
        var status = (int)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        _ = await Assert.That(status).IsEqualTo((int)OutboxMessageStatus.Pending);
    }

    [Test]
    public async Task StoreAsync_SerializesPayloadCorrectly()
    {
        var connectionString = _fixture.GetConnectionString(_databaseName);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var options = Options.Create(new OutboxOptions());
        var outbox = new SqlServerEventOutbox(connection, options, TimeProvider.System);

        var @event = new TestSqlOutboxEvent("payload-1", "Test payload data");

        await outbox.StoreAsync(@event).ConfigureAwait(false);

        // Verify payload contains expected data
        await using var verifyConnection = new SqlConnection(connectionString);
        await verifyConnection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            "SELECT [Payload] FROM [pulse].[OutboxMessage] WHERE [EventType] LIKE '%TestSqlOutboxEvent%'",
            verifyConnection
        );
        var payload = (string)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;

        using (Assert.Multiple())
        {
            _ = await Assert.That(payload).Contains("payload-1");
            _ = await Assert.That(payload).Contains("Test payload data");
        }
    }

    #endregion

    #region Test Event

    /// <summary>
    /// Test event for SQL Server event outbox integration tests.
    /// </summary>
    internal sealed class TestSqlOutboxEvent : IEvent
    {
        public TestSqlOutboxEvent() { }

        public TestSqlOutboxEvent(string id, string data)
        {
            Id = id;
            Data = data;
        }

        public string Id { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    #endregion
}
