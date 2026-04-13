namespace NetEvolve.Pulse.Outbox;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQL Server implementation of <see cref="IEventOutbox"/> that stores events using ADO.NET
/// with support for enlisting in existing <see cref="SqlTransaction"/> instances.
/// </summary>
/// <remarks>
/// <para><strong>Transaction Integration:</strong></para>
/// This implementation can participate in an existing SQL Server transaction by:
/// <list type="bullet">
/// <item><description>Accepting a <see cref="SqlConnection"/> with active transaction via constructor</description></item>
/// <item><description>Using <see cref="IOutboxTransactionScope"/> for ambient transaction support</description></item>
/// </list>
/// <para><strong>Atomicity:</strong></para>
/// When called within a transaction, the event is stored atomically with business data.
/// If the transaction rolls back, the event is also discarded.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL constructed from validated OutboxOptions properties and strongly-typed parameters, not user input."
)]
public sealed class SqlServerEventOutbox : IEventOutbox
{
    /// <summary>The open SQL connection provided explicitly; null when using the DI-friendly constructor.</summary>
    private readonly SqlConnection? _explicitConnection;

    /// <summary>The optional SQL transaction provided explicitly; null when using the DI-friendly constructor.</summary>
    private readonly SqlTransaction? _explicitTransaction;

    /// <summary>The connection string used by the DI-friendly constructor; null when using the explicit-connection constructor.</summary>
    private readonly string? _connectionString;

    /// <summary>The optional transaction scope used by the DI-friendly constructor to obtain an ambient transaction.</summary>
    private readonly IOutboxTransactionScope? _transactionScope;

    /// <summary>The resolved outbox options controlling table name, schema, and JSON serialization.</summary>
    private readonly OutboxOptions _options;

    /// <summary>The time provider used to generate consistent creation and update timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Represents the SQL statement used for inserting data into a database table.</summary>
    private readonly string _sqlInsertInto;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerEventOutbox"/> class using an explicit connection.
    /// </summary>
    /// <param name="connection">The SQL connection (should already be open).</param>
    /// <param name="options">The outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    /// <param name="transaction">Optional transaction to enlist with.</param>
    public SqlServerEventOutbox(
        SqlConnection connection,
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        SqlTransaction? transaction = null
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _explicitConnection = connection;
        _explicitTransaction = transaction;
        _options = options.Value;
        _timeProvider = timeProvider;

        _sqlInsertInto = BuildInsertSql(_options);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerEventOutbox"/> class for use with dependency injection.
    /// Opens its own <see cref="SqlConnection"/> on each <see cref="StoreAsync{TEvent}"/> call and enlists in
    /// an active <see cref="SqlTransaction"/> from <paramref name="transactionScope"/> when present.
    /// </summary>
    /// <param name="options">The outbox configuration options (must include a connection string).</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    /// <param name="transactionScope">Optional ambient transaction scope; when active, the event is stored within the same transaction.</param>
    public SqlServerEventOutbox(
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        IOutboxTransactionScope? transactionScope = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _connectionString = options.Value.ConnectionString;
        _transactionScope = transactionScope;
        _options = options.Value;
        _timeProvider = timeProvider;

        _sqlInsertInto = BuildInsertSql(_options);
    }

    /// <inheritdoc />
    public async Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();
        var eventType =
            messageType.AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Cannot get assembly-qualified name for type: {messageType}");

        if (eventType.Length > OutboxMessageSchema.MaxLengths.EventType)
        {
            throw new InvalidOperationException(
                $"Event type identifier exceeds the EventType column maximum length of {OutboxMessageSchema.MaxLengths.EventType} characters. "
                    + "Shorten the type identifier, increase the database column length, or use Type.FullName with a type registry."
            );
        }

        var correlationId = message.CorrelationId;

        if (correlationId is { Length: > OutboxMessageSchema.MaxLengths.CorrelationId })
        {
            throw new InvalidOperationException(
                $"CorrelationId exceeds the maximum length of {OutboxMessageSchema.MaxLengths.CorrelationId} characters defined by the OutboxMessage schema. "
                    + "Provide a shorter correlation identifier to comply with the database constraint."
            );
        }

        var now = _timeProvider.GetUtcNow();
        var payload = JsonSerializer.Serialize(message, messageType, _options.JsonSerializerOptions);

        if (_explicitConnection is not null)
        {
            await using var command = new SqlCommand(_sqlInsertInto, _explicitConnection, _explicitTransaction);
            AddParameters(command, message, eventType, correlationId, now, payload);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var currentTransaction = _transactionScope?.GetCurrentTransaction();

            if (currentTransaction is not null and not SqlTransaction)
            {
                throw new InvalidOperationException(
                    $"IOutboxTransactionScope returned a transaction of type '{currentTransaction.GetType().Name}', but SqlServerEventOutbox requires a SqlTransaction."
                );
            }

            var ambientTransaction = currentTransaction as SqlTransaction;

            if (ambientTransaction is not null)
            {
                var connection =
                    ambientTransaction.Connection
                    ?? throw new InvalidOperationException("Transaction has no associated connection.");

                await using var command = new SqlCommand(_sqlInsertInto, connection, ambientTransaction);
                AddParameters(command, message, eventType, correlationId, now, payload);
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand(_sqlInsertInto, connection);
                AddParameters(command, message, eventType, correlationId, now, payload);
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string BuildInsertSql(OutboxOptions options) =>
        $"""
            INSERT INTO {options.FullTableName}
                ([{OutboxMessageSchema.Columns.Id}],
                 [{OutboxMessageSchema.Columns.EventType}],
                 [{OutboxMessageSchema.Columns.Payload}],
                 [{OutboxMessageSchema.Columns.CorrelationId}],
                 [{OutboxMessageSchema.Columns.CreatedAt}],
                 [{OutboxMessageSchema.Columns.UpdatedAt}],
                 [{OutboxMessageSchema.Columns.ProcessedAt}],
                 [{OutboxMessageSchema.Columns.RetryCount}],
                 [{OutboxMessageSchema.Columns.Error}],
                 [{OutboxMessageSchema.Columns.Status}])
            VALUES
                (@Id, @EventType, @Payload, @CorrelationId, @CreatedAt, @UpdatedAt, NULL, 0, NULL, @Status)
            """;

    private static void AddParameters(
        SqlCommand command,
        IEvent message,
        string eventType,
        string? correlationId,
        DateTimeOffset now,
        string payload
    )
    {
        _ = command.Parameters.AddWithValue("@Id", message.ToOutboxId());
        _ = command.Parameters.AddWithValue("@EventType", eventType);
        _ = command.Parameters.AddWithValue("@Payload", payload);
        _ = command.Parameters.AddWithValue("@CorrelationId", (object?)correlationId ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@CreatedAt", now);
        _ = command.Parameters.AddWithValue("@UpdatedAt", now);
        _ = command.Parameters.AddWithValue("@Status", OutboxMessageStatus.Pending);
    }
}
