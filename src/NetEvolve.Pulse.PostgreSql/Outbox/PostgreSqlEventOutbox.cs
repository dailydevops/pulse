namespace NetEvolve.Pulse.Outbox;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of <see cref="IEventOutbox"/> using ADO.NET.
/// Stores events directly using a <see cref="NpgsqlConnection"/>, optionally enlisting in an
/// active <see cref="NpgsqlTransaction"/> provided by <see cref="IOutboxTransactionScope"/>.
/// </summary>
/// <remarks>
/// <para><strong>Transaction Integration:</strong></para>
/// When an <see cref="IOutboxTransactionScope"/> is registered and returns a non-null
/// <see cref="NpgsqlTransaction"/>, the INSERT is executed on that transaction's connection so
/// that the event store participates in the caller's ambient transaction.
/// <para><strong>Standalone Mode:</strong></para>
/// When no transaction scope is active, a new <see cref="NpgsqlConnection"/> is opened for the
/// INSERT and disposed immediately after the operation completes.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Table name is constructed from validated OutboxOptions properties, not user input."
)]
public sealed class PostgreSqlEventOutbox : IEventOutbox
{
    /// <summary>The PostgreSQL connection string used when no ambient transaction is present.</summary>
    private readonly string _connectionString;

    /// <summary>The resolved outbox options controlling serialization and table configuration.</summary>
    private readonly OutboxOptions _options;

    /// <summary>The time provider used to generate consistent creation and update timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The optional transaction scope providing an ambient <see cref="NpgsqlTransaction"/>.</summary>
    private readonly IOutboxTransactionScope? _transactionScope;

    /// <summary>Cached INSERT SQL statement built once from the configured table name.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlEventOutbox"/> class.
    /// </summary>
    /// <param name="options">The outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    /// <param name="transactionScope">Optional transaction scope for ambient transaction support.</param>
    public PostgreSqlEventOutbox(
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        IOutboxTransactionScope? transactionScope = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options.Value;
        _connectionString = _options.ConnectionString;
        _timeProvider = timeProvider;
        _transactionScope = transactionScope;

        _insertSql = $"""
            INSERT INTO {_options.FullTableName}
                ("{OutboxMessageSchema.Columns.Id}",
                 "{OutboxMessageSchema.Columns.EventType}",
                 "{OutboxMessageSchema.Columns.Payload}",
                 "{OutboxMessageSchema.Columns.CorrelationId}",
                 "{OutboxMessageSchema.Columns.CreatedAt}",
                 "{OutboxMessageSchema.Columns.UpdatedAt}",
                 "{OutboxMessageSchema.Columns.ProcessedAt}",
                 "{OutboxMessageSchema.Columns.NextRetryAt}",
                 "{OutboxMessageSchema.Columns.RetryCount}",
                 "{OutboxMessageSchema.Columns.Error}",
                 "{OutboxMessageSchema.Columns.Status}")
            VALUES
                (@Id, @EventType, @Payload, @CorrelationId, @CreatedAt, @UpdatedAt, @ProcessedAt, @NextRetryAt, @RetryCount, @Error, @Status)
            """;
    }

    /// <inheritdoc />
    public async Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();

        var correlationId = message.CorrelationId;

        if (correlationId is { Length: > OutboxMessageSchema.MaxLengths.CorrelationId })
        {
            throw new InvalidOperationException(
                $"CorrelationId exceeds the maximum length of {OutboxMessageSchema.MaxLengths.CorrelationId} characters defined by the OutboxMessage schema. "
                    + "Provide a shorter correlation identifier to comply with the database constraint."
            );
        }

        var now = _timeProvider.GetUtcNow();

        var outboxMessage = new OutboxMessage
        {
            Id = message.ToOutboxId(),
            EventType = messageType,
            Payload = JsonSerializer.Serialize(message, messageType, _options.JsonSerializerOptions),
            CorrelationId = correlationId,
            CreatedAt = now,
            UpdatedAt = now,
            Status = OutboxMessageStatus.Pending,
        };

        var transaction = GetCurrentTransaction();

        if (transaction is not null)
        {
            var connection =
                transaction.Connection
                ?? throw new InvalidOperationException("Transaction has no associated connection.");

            await using var command = new NpgsqlCommand(_insertSql, connection, transaction);
            AddMessageParameters(command, outboxMessage);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(_insertSql, connection);
            AddMessageParameters(command, outboxMessage);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Retrieves the current ambient <see cref="NpgsqlTransaction"/> from the registered
    /// <see cref="IOutboxTransactionScope"/>, or <see langword="null"/> if no scope is configured
    /// or no transaction is active.
    /// </summary>
    /// <returns>The current <see cref="NpgsqlTransaction"/>, or <see langword="null"/>.</returns>
    private NpgsqlTransaction? GetCurrentTransaction() =>
        _transactionScope?.GetCurrentTransaction() as NpgsqlTransaction;

    /// <summary>
    /// Adds all <see cref="OutboxMessage"/> property values as typed parameters to a <see cref="NpgsqlCommand"/>.
    /// Null-valued optional columns are mapped to <see cref="DBNull.Value"/>.
    /// </summary>
    /// <param name="command">The command to which parameters are added.</param>
    /// <param name="message">The outbox message providing parameter values.</param>
    private static void AddMessageParameters(NpgsqlCommand command, OutboxMessage message)
    {
        _ = command.Parameters.AddWithValue("Id", message.Id);
        _ = command.Parameters.AddWithValue("EventType", message.EventType.ToOutboxEventTypeName());
        _ = command.Parameters.AddWithValue("Payload", message.Payload);
        _ = command.Parameters.AddWithValue("CorrelationId", (object?)message.CorrelationId ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("CreatedAt", message.CreatedAt);
        _ = command.Parameters.AddWithValue("UpdatedAt", message.UpdatedAt);
        _ = command.Parameters.AddWithValue(
            "ProcessedAt",
            message.ProcessedAt.HasValue ? message.ProcessedAt.Value : DBNull.Value
        );
        _ = command.Parameters.AddWithValue(
            "NextRetryAt",
            message.NextRetryAt.HasValue ? message.NextRetryAt.Value : DBNull.Value
        );
        _ = command.Parameters.AddWithValue("RetryCount", message.RetryCount);
        _ = command.Parameters.AddWithValue("Error", (object?)message.Error ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("Status", (int)message.Status);
    }
}
