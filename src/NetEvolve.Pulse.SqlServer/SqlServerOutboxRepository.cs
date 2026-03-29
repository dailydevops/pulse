namespace NetEvolve.Pulse;

using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// SQL Server implementation of <see cref="IOutboxRepository"/> using ADO.NET.
/// Provides optimized T-SQL operations with proper transaction and locking support.
/// </summary>
/// <remarks>
/// <para><strong>Transaction Support:</strong></para>
/// <see cref="AddAsync"/> can participate in ambient transactions via <see cref="IOutboxTransactionScope"/>
/// or by using the connection with an active transaction.
/// <para><strong>Concurrency:</strong></para>
/// Uses ROWLOCK and READPAST hints to prevent conflicts during concurrent polling.
/// <para><strong>Performance:</strong></para>
/// Leverages stored procedures for efficient batch operations and index utilization.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Stored procedure names are constructed from validated OutboxOptions.Schema property, not user input."
)]
[SuppressMessage(
    "Roslynator",
    "RCS1084:Use coalesce expression instead of conditional expression",
    Justification = "NextRetryAt and ProcessedAt properties require explicit conditional checks."
)]
internal sealed class SqlServerOutboxRepository : IOutboxRepository
{
    /// <summary>The SQL Server connection string used to open new connections for each repository operation.</summary>
    private readonly string _connectionString;

    /// <summary>The optional transaction scope providing an ambient <see cref="SqlTransaction"/> for <see cref="AddAsync"/>.</summary>
    private readonly IOutboxTransactionScope? _transactionScope;

    /// <summary>The time provider used to generate consistent timestamps for cutoff calculations.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Cached stored procedure name for retrieving pending outbox messages.</summary>
    private readonly string _getPendingSql;

    /// <summary>Cached stored procedure name for retrieving failed outbox messages eligible for retry.</summary>
    private readonly string _getFailedForRetrySql;

    /// <summary>Cached stored procedure name for marking an outbox message as completed.</summary>
    private readonly string _markCompletedSql;

    /// <summary>Cached stored procedure name for marking an outbox message as failed.</summary>
    private readonly string _markFailedSql;

    /// <summary>Cached stored procedure name for moving an outbox message to dead letter.</summary>
    private readonly string _markDeadLetterSql;

    /// <summary>Cached stored procedure name for deleting completed outbox messages.</summary>
    private readonly string _deleteCompletedSql;

    /// <summary>Cached SQL command text for inserting a new outbox message when no ambient transaction is present.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerOutboxRepository"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="options">The outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    /// <param name="transactionScope">Optional transaction scope for ambient transaction support.</param>
    public SqlServerOutboxRepository(
        string connectionString,
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        IOutboxTransactionScope? transactionScope = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _connectionString = connectionString;
        _timeProvider = timeProvider;
        _transactionScope = transactionScope;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? OutboxMessageSchema.DefaultSchema
            : options.Value.Schema;
        _getPendingSql = $"[{schema}].[usp_GetPendingOutboxMessages]";
        _getFailedForRetrySql = $"[{schema}].[usp_GetFailedOutboxMessagesForRetry]";
        _markCompletedSql = $"[{schema}].[usp_MarkOutboxMessageCompleted]";
        _markFailedSql = $"[{schema}].[usp_MarkOutboxMessageFailed]";
        _markDeadLetterSql = $"[{schema}].[usp_MarkOutboxMessageDeadLetter]";
        _deleteCompletedSql = $"[{schema}].[usp_DeleteCompletedOutboxMessages]";

        _insertSql = $"""
            INSERT INTO {options.Value.FullTableName}
                ([{OutboxMessageSchema.Columns.Id}],
                 [{OutboxMessageSchema.Columns.EventType}],
                 [{OutboxMessageSchema.Columns.Payload}],
                 [{OutboxMessageSchema.Columns.CorrelationId}],
                 [{OutboxMessageSchema.Columns.CreatedAt}],
                 [{OutboxMessageSchema.Columns.UpdatedAt}],
                 [{OutboxMessageSchema.Columns.ProcessedAt}],
                 [{OutboxMessageSchema.Columns.NextRetryAt}],
                 [{OutboxMessageSchema.Columns.RetryCount}],
                 [{OutboxMessageSchema.Columns.Error}],
                 [{OutboxMessageSchema.Columns.Status}])
            VALUES
                (@Id, @EventType, @Payload, @CorrelationId, @CreatedAt, @UpdatedAt, @ProcessedAt, @NextRetryAt, @RetryCount, @Error, @Status)
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

            await using var command = new SqlCommand(_insertSql, connection, transaction);
            AddMessageParameters(command, message);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Create a new connection when no ambient transaction exists
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(_insertSql, connection);
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
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_getPendingSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@batchSize", batchSize);
        _ = command.Parameters.AddWithValue("@nowUtc", now);

        return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
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
        await using var command = new SqlCommand(_getFailedForRetrySql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@maxRetryCount", maxRetryCount);
        _ = command.Parameters.AddWithValue("@batchSize", batchSize);
        _ = command.Parameters.AddWithValue("@nowUtc", now);

        return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_markCompletedSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@messageId", messageId);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_markFailedSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@messageId", messageId);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        IReadOnlyCollection<Guid> messageIds,
        string errorMessage,
        CancellationToken cancellationToken = default
    ) =>
        await Parallel
            .ForEachAsync(
                messageIds,
                cancellationToken,
                async (id, token) => await MarkAsFailedAsync(id, errorMessage, token).ConfigureAwait(false)
            )
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        DateTimeOffset? nextRetryAt,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_markFailedSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@messageId", messageId);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@nextRetryAt", nextRetryAt.HasValue ? nextRetryAt.Value : DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsDeadLetterAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_markDeadLetterSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@messageId", messageId);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoffTime = _timeProvider.GetUtcNow().Subtract(olderThan);

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_deleteCompletedSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@olderThanUtc", cutoffTime);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int count ? count : 0;
    }

    /// <summary>
    /// Opens and returns a new <see cref="SqlConnection"/> using the stored connection string.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An open <see cref="SqlConnection"/>.</returns>
    private async Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Retrieves the current ambient <see cref="SqlTransaction"/> from the registered
    /// <see cref="IOutboxTransactionScope"/>, or <see langword="null"/> if no scope is configured
    /// or no transaction is active.
    /// </summary>
    /// <returns>The current <see cref="SqlTransaction"/>, or <see langword="null"/>.</returns>
    private SqlTransaction? GetCurrentTransaction() => _transactionScope?.GetCurrentTransaction() as SqlTransaction;

    /// <summary>
    /// Adds all <see cref="OutboxMessage"/> property values as typed parameters to a <see cref="SqlCommand"/>.
    /// Null-valued optional columns (<see cref="OutboxMessage.CorrelationId"/>, <see cref="OutboxMessage.Error"/>,
    /// <see cref="OutboxMessage.ProcessedAt"/>, <see cref="OutboxMessage.NextRetryAt"/>) are mapped to <see cref="DBNull.Value"/>.
    /// </summary>
    /// <param name="command">The command to which parameters are added.</param>
    /// <param name="message">The outbox message providing parameter values.</param>
    private static void AddMessageParameters(SqlCommand command, OutboxMessage message)
    {
        _ = command.Parameters.AddWithValue("@Id", message.Id);
        _ = command.Parameters.AddWithValue("@EventType", message.EventType);
        _ = command.Parameters.AddWithValue("@Payload", message.Payload);
        _ = command.Parameters.AddWithValue("@CorrelationId", (object?)message.CorrelationId ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@CreatedAt", message.CreatedAt);
        _ = command.Parameters.AddWithValue("@UpdatedAt", message.UpdatedAt);
        _ = command.Parameters.AddWithValue(
            "@ProcessedAt",
            message.ProcessedAt.HasValue ? message.ProcessedAt.Value : DBNull.Value
        );
        _ = command.Parameters.AddWithValue(
            "@NextRetryAt",
            message.NextRetryAt.HasValue ? message.NextRetryAt.Value : DBNull.Value
        );
        _ = command.Parameters.AddWithValue("@RetryCount", message.RetryCount);
        _ = command.Parameters.AddWithValue("@Error", (object?)message.Error ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@Status", (int)message.Status);
    }

    /// <summary>
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="OutboxMessage"/> instances
    /// using <see cref="MapToMessage"/>. Column ordinals are resolved once per result set to avoid
    /// repeated string lookups on every row.
    /// </summary>
    /// <param name="command">The <see cref="SqlCommand"/> to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of <see cref="OutboxMessage"/> records.</returns>
    private static async Task<IReadOnlyList<OutboxMessage>> ReadMessagesAsync(
        SqlCommand command,
        CancellationToken cancellationToken
    )
    {
        var messages = new List<OutboxMessage>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return messages;
        }

        // Resolve ordinals once per result set � GetOrdinal is a string lookup
        var ordId = reader.GetOrdinal(OutboxMessageSchema.Columns.Id);
        var ordEventType = reader.GetOrdinal(OutboxMessageSchema.Columns.EventType);
        var ordPayload = reader.GetOrdinal(OutboxMessageSchema.Columns.Payload);
        var ordCorrelationId = reader.GetOrdinal(OutboxMessageSchema.Columns.CorrelationId);
        var ordCreatedAt = reader.GetOrdinal(OutboxMessageSchema.Columns.CreatedAt);
        var ordUpdatedAt = reader.GetOrdinal(OutboxMessageSchema.Columns.UpdatedAt);
        var ordProcessedAt = reader.GetOrdinal(OutboxMessageSchema.Columns.ProcessedAt);
        var ordNextRetryAt = reader.GetOrdinal(OutboxMessageSchema.Columns.NextRetryAt);
        var ordRetryCount = reader.GetOrdinal(OutboxMessageSchema.Columns.RetryCount);
        var ordError = reader.GetOrdinal(OutboxMessageSchema.Columns.Error);
        var ordStatus = reader.GetOrdinal(OutboxMessageSchema.Columns.Status);

        do
        {
            messages.Add(
                MapToMessage(
                    reader,
                    ordId,
                    ordEventType,
                    ordPayload,
                    ordCorrelationId,
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
    /// Maps the current row of a <see cref="SqlDataReader"/> to a new <see cref="OutboxMessage"/> instance
    /// using pre-resolved column ordinals for maximum performance.
    /// </summary>
    /// <param name="reader">The reader positioned on the row to map.</param>
    /// <param name="ordId">Pre-resolved ordinal for the Id column.</param>
    /// <param name="ordEventType">Pre-resolved ordinal for the EventType column.</param>
    /// <param name="ordPayload">Pre-resolved ordinal for the Payload column.</param>
    /// <param name="ordCorrelationId">Pre-resolved ordinal for the CorrelationId column.</param>
    /// <param name="ordCreatedAt">Pre-resolved ordinal for the CreatedAt column.</param>
    /// <param name="ordUpdatedAt">Pre-resolved ordinal for the UpdatedAt column.</param>
    /// <param name="ordProcessedAt">Pre-resolved ordinal for the ProcessedAt column.</param>
    /// <param name="ordNextRetryAt">Pre-resolved ordinal for the NextRetryAt column.</param>
    /// <param name="ordRetryCount">Pre-resolved ordinal for the RetryCount column.</param>
    /// <param name="ordError">Pre-resolved ordinal for the Error column.</param>
    /// <param name="ordStatus">Pre-resolved ordinal for the Status column.</param>
    /// <returns>A populated <see cref="OutboxMessage"/>.</returns>
    private static OutboxMessage MapToMessage(
        SqlDataReader reader,
        int ordId,
        int ordEventType,
        int ordPayload,
        int ordCorrelationId,
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
            EventType = reader.GetString(ordEventType),
            Payload = reader.GetString(ordPayload),
            CorrelationId = reader.IsDBNull(ordCorrelationId) ? null : reader.GetString(ordCorrelationId),
            CreatedAt = reader.GetDateTimeOffset(ordCreatedAt),
            UpdatedAt = reader.GetDateTimeOffset(ordUpdatedAt),
            ProcessedAt = reader.IsDBNull(ordProcessedAt) ? null : reader.GetDateTimeOffset(ordProcessedAt),
            NextRetryAt = reader.IsDBNull(ordNextRetryAt) ? null : reader.GetDateTimeOffset(ordNextRetryAt),
            RetryCount = reader.GetInt32(ordRetryCount),
            Error = reader.IsDBNull(ordError) ? null : reader.GetString(ordError),
            Status = (OutboxMessageStatus)reader.GetInt32(ordStatus),
        };
}
