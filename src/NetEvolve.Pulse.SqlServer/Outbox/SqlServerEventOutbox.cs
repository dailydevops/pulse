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
    /// <summary>The open SQL connection used to execute insert commands.</summary>
    private readonly SqlConnection _connection;

    /// <summary>The optional SQL transaction to enlist with, ensuring atomicity with business operations.</summary>
    private readonly SqlTransaction? _transaction;

    /// <summary>The resolved outbox options controlling table name, schema, and JSON serialization.</summary>
    private readonly OutboxOptions _options;

    /// <summary>The time provider used to generate consistent creation and update timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Represents the SQL statement used for inserting data into a database table.</summary>
    private readonly string _sqlInsertInto;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerEventOutbox"/> class.
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

        _connection = connection;
        _options = options.Value;
        _timeProvider = timeProvider;
        _transaction = transaction;

        _sqlInsertInto = $"""
            INSERT INTO {_options.FullTableName}
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

        await using var command = new SqlCommand(_sqlInsertInto, _connection, _transaction);

        var now = _timeProvider.GetUtcNow();
        var payload = JsonSerializer.Serialize(message, messageType, _options.JsonSerializerOptions);

        _ = command.Parameters.AddWithValue("@Id", message.ToOutboxId());
        _ = command.Parameters.AddWithValue("@EventType", eventType);
        _ = command.Parameters.AddWithValue("@Payload", payload);
        _ = command.Parameters.AddWithValue("@CorrelationId", (object?)correlationId ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@CreatedAt", now);
        _ = command.Parameters.AddWithValue("@UpdatedAt", now);
        _ = command.Parameters.AddWithValue("@Status", OutboxMessageStatus.Pending);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
