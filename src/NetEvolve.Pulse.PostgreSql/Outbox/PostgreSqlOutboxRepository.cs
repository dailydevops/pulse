namespace NetEvolve.Pulse.Outbox;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of <see cref="IOutboxRepository"/> using ADO.NET.
/// Provides optimized PostgreSQL operations with proper transaction and locking support.
/// </summary>
/// <remarks>
/// <para><strong>Transaction Support:</strong></para>
/// <see cref="AddAsync"/> can participate in ambient transactions via <see cref="IOutboxTransactionScope"/>
/// or by using the connection with an active transaction.
/// <para><strong>Concurrency:</strong></para>
/// Uses FOR UPDATE SKIP LOCKED to prevent conflicts during concurrent polling.
/// <para><strong>Performance:</strong></para>
/// Leverages stored functions for efficient batch operations and index utilization.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Stored function names are constructed from validated OutboxOptions.Schema property, not user input."
)]
[SuppressMessage(
    "Roslynator",
    "RCS1084:Use coalesce expression instead of conditional expression",
    Justification = "NextRetryAt and ProcessedAt properties require explicit conditional checks."
)]
internal sealed class PostgreSqlOutboxRepository : IOutboxRepository
{
    /// <summary>The PostgreSQL connection string used to open new connections for each repository operation.</summary>
    private readonly string _connectionString;

    /// <summary>The optional transaction scope providing an ambient <see cref="NpgsqlTransaction"/> for <see cref="AddAsync"/>.</summary>
    private readonly IOutboxTransactionScope? _transactionScope;

    /// <summary>The time provider used to generate consistent timestamps for cutoff calculations.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Cached SQL for calling the get_pending_outbox_messages function.</summary>
    private readonly string _getPendingSql;

    /// <summary>Cached SQL for calling the get_failed_outbox_messages_for_retry function.</summary>
    private readonly string _getFailedForRetrySql;

    /// <summary>Cached SQL for calling the mark_outbox_message_completed function.</summary>
    private readonly string _markCompletedSql;

    /// <summary>Cached SQL for calling the mark_outbox_message_failed function.</summary>
    private readonly string _markFailedSql;

    /// <summary>Cached SQL for calling the mark_outbox_message_dead_letter function.</summary>
    private readonly string _markDeadLetterSql;

    /// <summary>Cached SQL for calling the delete_completed_outbox_messages function.</summary>
    private readonly string _deleteCompletedSql;

    /// <summary>Cached SQL for inserting into Outbox Table.</summary>
    private readonly string _insertSql;

    /// <summary>Cached SQL for counting pending outbox messages.</summary>
    private readonly string _getPendingCountSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxRepository"/> class.
    /// </summary>
    /// <param name="options">The outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    /// <param name="transactionScope">Optional transaction scope for ambient transaction support.</param>
    public PostgreSqlOutboxRepository(
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        IOutboxTransactionScope? transactionScope = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _connectionString = options.Value.ConnectionString;
        _timeProvider = timeProvider;
        _transactionScope = transactionScope;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? OutboxMessageSchema.DefaultSchema
            : options.Value.Schema;
        _getPendingSql = $"SELECT * FROM \"{schema}\".get_pending_outbox_messages(@batch_size)";
        _getFailedForRetrySql =
            $"SELECT * FROM \"{schema}\".get_failed_outbox_messages_for_retry(@max_retry_count, @batch_size, @now_utc)";
        _markCompletedSql =
            $"SELECT \"{schema}\".mark_outbox_message_completed(@message_id, @processed_at, @updated_at)";
        _markFailedSql = $"SELECT \"{schema}\".mark_outbox_message_failed(@message_id, @error, @next_retry_at)";
        _markDeadLetterSql = $"SELECT \"{schema}\".mark_outbox_message_dead_letter(@message_id, @error)";
        _deleteCompletedSql = $"SELECT \"{schema}\".delete_completed_outbox_messages(@older_than_utc)";
        _getPendingCountSql =
            $"SELECT COUNT(*) FROM {options.Value.FullTableName} WHERE \"{OutboxMessageSchema.Columns.Status}\" = 0";

        _insertSql = $"""
            INSERT INTO {options.Value.FullTableName}
                ("{OutboxMessageSchema.Columns.Id}",
                 "{OutboxMessageSchema.Columns.EventType}",
                 "{OutboxMessageSchema.Columns.Payload}",
                 "{OutboxMessageSchema.Columns.CorrelationId}",
                 "{OutboxMessageSchema.Columns.CausationId}",
                 "{OutboxMessageSchema.Columns.CreatedAt}",
                 "{OutboxMessageSchema.Columns.UpdatedAt}",
                 "{OutboxMessageSchema.Columns.ProcessedAt}",
                 "{OutboxMessageSchema.Columns.NextRetryAt}",
                 "{OutboxMessageSchema.Columns.RetryCount}",
                 "{OutboxMessageSchema.Columns.Error}",
                 "{OutboxMessageSchema.Columns.Status}")
            VALUES
                (@Id, @EventType, @Payload, @CorrelationId, @CausationId, @CreatedAt, @UpdatedAt, @ProcessedAt, @NextRetryAt, @RetryCount, @Error, @Status)
            """;
    }

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var transaction = GetCurrentTransaction();

        if (transaction is not null)
        {
            // Use the connection from the ambient transaction to avoid mismatch
            var connection =
                transaction.Connection
                ?? throw new InvalidOperationException("Transaction has no associated connection.");

            await using var command = new NpgsqlCommand(_insertSql, connection, transaction);
            AddMessageParameters(command, message);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Create a new connection when no ambient transaction exists
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(_insertSql, connection);
            AddMessageParameters(command, message);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_getPendingSql, connection);

        _ = command.Parameters.AddWithValue("batch_size", batchSize);

        return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_getPendingCountSql, connection);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count
            ? count
            : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_getFailedForRetrySql, connection);

        _ = command.Parameters.AddWithValue("max_retry_count", maxRetryCount);
        _ = command.Parameters.AddWithValue("batch_size", batchSize);
        _ = command.Parameters.AddWithValue("now_utc", now);

        return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_markCompletedSql, connection);

        _ = command.Parameters.AddWithValue("message_id", messageId);
        _ = command.Parameters.AddWithValue("processed_at", now);
        _ = command.Parameters.AddWithValue("updated_at", now);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var messageId in messageIds)
        {
            await using var command = new NpgsqlCommand(_markCompletedSql, connection);
            _ = command.Parameters.AddWithValue("message_id", messageId);
            _ = command.Parameters.AddWithValue("processed_at", now);
            _ = command.Parameters.AddWithValue("updated_at", now);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_markFailedSql, connection);

        _ = command.Parameters.AddWithValue("message_id", messageId);
        _ = command.Parameters.AddWithValue("error", (object?)errorMessage ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("next_retry_at", DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        DateTimeOffset? nextRetryAt,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_markFailedSql, connection);

        _ = command.Parameters.AddWithValue("message_id", messageId);
        _ = command.Parameters.AddWithValue("error", (object?)errorMessage ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("next_retry_at", nextRetryAt.HasValue ? nextRetryAt.Value : DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        IReadOnlyCollection<Guid> messageIds,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var messageId in messageIds)
        {
            await using var command = new NpgsqlCommand(_markFailedSql, connection);
            _ = command.Parameters.AddWithValue("message_id", messageId);
            _ = command.Parameters.AddWithValue("error", (object?)errorMessage ?? DBNull.Value);
            _ = command.Parameters.AddWithValue("next_retry_at", DBNull.Value);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task MarkAsDeadLetterAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_markDeadLetterSql, connection);

        _ = command.Parameters.AddWithValue("message_id", messageId);
        _ = command.Parameters.AddWithValue("error", (object?)errorMessage ?? DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoffTime = _timeProvider.GetUtcNow().Subtract(olderThan);

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_deleteCompletedSql, connection);

        _ = command.Parameters.AddWithValue("older_than_utc", cutoffTime);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int count ? count : 0;
    }

    /// <summary>
    /// Opens and returns a new <see cref="NpgsqlConnection"/> using the stored connection string.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An open <see cref="NpgsqlConnection"/>.</returns>
    private async Task<NpgsqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
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
    /// Null-valued optional columns (<see cref="OutboxMessage.CorrelationId"/>, <see cref="OutboxMessage.CausationId"/>,
    /// <see cref="OutboxMessage.Error"/>, <see cref="OutboxMessage.ProcessedAt"/>,
    /// <see cref="OutboxMessage.NextRetryAt"/>) are mapped to <see cref="DBNull.Value"/>.
    /// </summary>
    /// <param name="command">The command to which parameters are added.</param>
    /// <param name="message">The outbox message providing parameter values.</param>
    private static void AddMessageParameters(NpgsqlCommand command, OutboxMessage message)
    {
        _ = command.Parameters.AddWithValue("Id", message.Id);
        _ = command.Parameters.AddWithValue("EventType", message.EventType.ToOutboxEventTypeName());
        _ = command.Parameters.AddWithValue("Payload", message.Payload);
        _ = command.Parameters.AddWithValue("CorrelationId", (object?)message.CorrelationId ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("CausationId", (object?)message.CausationId ?? DBNull.Value);
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

    /// <summary>
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="OutboxMessage"/> instances
    /// using <see cref="MapToMessage"/>. Column ordinals are resolved once per result set to avoid
    /// repeated string lookups on every row.
    /// </summary>
    /// <param name="command">The <see cref="NpgsqlCommand"/> to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of <see cref="OutboxMessage"/> records.</returns>
    private static async Task<IReadOnlyList<OutboxMessage>> ReadMessagesAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        // Resolve ordinals once per result set — GetOrdinal is a string lookup
        var ordId = reader.GetOrdinal(OutboxMessageSchema.Columns.Id);
        var ordEventType = reader.GetOrdinal(OutboxMessageSchema.Columns.EventType);
        var ordPayload = reader.GetOrdinal(OutboxMessageSchema.Columns.Payload);
        var ordCorrelationId = reader.GetOrdinal(OutboxMessageSchema.Columns.CorrelationId);
        var ordCausationId = reader.GetOrdinal(OutboxMessageSchema.Columns.CausationId);
        var ordCreatedAt = reader.GetOrdinal(OutboxMessageSchema.Columns.CreatedAt);
        var ordUpdatedAt = reader.GetOrdinal(OutboxMessageSchema.Columns.UpdatedAt);
        var ordProcessedAt = reader.GetOrdinal(OutboxMessageSchema.Columns.ProcessedAt);
        var ordNextRetryAt = reader.GetOrdinal(OutboxMessageSchema.Columns.NextRetryAt);
        var ordRetryCount = reader.GetOrdinal(OutboxMessageSchema.Columns.RetryCount);
        var ordError = reader.GetOrdinal(OutboxMessageSchema.Columns.Error);
        var ordStatus = reader.GetOrdinal(OutboxMessageSchema.Columns.Status);

        var messages = new List<OutboxMessage>();
        do
        {
            messages.Add(
                MapToMessage(
                    reader,
                    ordId,
                    ordEventType,
                    ordPayload,
                    ordCorrelationId,
                    ordCausationId,
                    ordCreatedAt,
                    ordUpdatedAt,
                    ordProcessedAt,
                    ordNextRetryAt,
                    ordRetryCount,
                    ordError,
                    ordStatus
                )
            );
        } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));

        return messages;
    }

    /// <summary>
    /// Maps the current row of a <see cref="NpgsqlDataReader"/> to a new <see cref="OutboxMessage"/> instance
    /// using pre-resolved column ordinals for maximum performance.
    /// </summary>
    /// <param name="reader">The reader positioned on the row to map.</param>
    /// <param name="ordId">Pre-resolved ordinal for the Id column.</param>
    /// <param name="ordEventType">Pre-resolved ordinal for the EventType column.</param>
    /// <param name="ordPayload">Pre-resolved ordinal for the Payload column.</param>
    /// <param name="ordCorrelationId">Pre-resolved ordinal for the CorrelationId column.</param>
    /// <param name="ordCausationId">Pre-resolved ordinal for the CausationId column.</param>
    /// <param name="ordCreatedAt">Pre-resolved ordinal for the CreatedAt column.</param>
    /// <param name="ordUpdatedAt">Pre-resolved ordinal for the UpdatedAt column.</param>
    /// <param name="ordProcessedAt">Pre-resolved ordinal for the ProcessedAt column.</param>
    /// <param name="ordNextRetryAt">Pre-resolved ordinal for the NextRetryAt column.</param>
    /// <param name="ordRetryCount">Pre-resolved ordinal for the RetryCount column.</param>
    /// <param name="ordError">Pre-resolved ordinal for the Error column.</param>
    /// <param name="ordStatus">Pre-resolved ordinal for the Status column.</param>
    /// <returns>A populated <see cref="OutboxMessage"/>.</returns>
    private static OutboxMessage MapToMessage(
        NpgsqlDataReader reader,
        int ordId,
        int ordEventType,
        int ordPayload,
        int ordCorrelationId,
        int ordCausationId,
        int ordCreatedAt,
        int ordUpdatedAt,
        int ordProcessedAt,
        int ordNextRetryAt,
        int ordRetryCount,
        int ordError,
        int ordStatus
    ) =>
        new OutboxMessage
        {
            Id = reader.GetGuid(ordId),
            EventType =
                Type.GetType(reader.GetString(ordEventType))
                ?? throw new InvalidOperationException(
                    $"Cannot resolve event type '{reader.GetString(ordEventType)}'."
                ),
            Payload = reader.GetString(ordPayload),
            CorrelationId = reader.IsDBNull(ordCorrelationId) ? null : reader.GetString(ordCorrelationId),
            CausationId = reader.IsDBNull(ordCausationId) ? null : reader.GetString(ordCausationId),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(ordCreatedAt),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(ordUpdatedAt),
            ProcessedAt = reader.IsDBNull(ordProcessedAt) ? null : reader.GetFieldValue<DateTimeOffset>(ordProcessedAt),
            NextRetryAt = reader.IsDBNull(ordNextRetryAt) ? null : reader.GetFieldValue<DateTimeOffset>(ordNextRetryAt),
            RetryCount = reader.GetInt32(ordRetryCount),
            Error = reader.IsDBNull(ordError) ? null : reader.GetString(ordError),
            Status = (OutboxMessageStatus)reader.GetInt32(ordStatus),
        };
}
