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
public sealed class SqlServerOutboxRepository : IOutboxRepository
{
    private readonly string _connectionString;
    private readonly OutboxOptions _options;
    private readonly IOutboxTransactionScope? _transactionScope;
    private readonly TimeProvider _timeProvider;

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
        _options = options.Value;
        _timeProvider = timeProvider;
        _transactionScope = transactionScope;
    }

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sql = $"""
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
                (@Id, @EventType, @Payload, @CorrelationId, @CreatedAt, @UpdatedAt, @ProcessedAt, @RetryCount, @Error, @Status)
            """;

        var transaction = GetCurrentTransaction();

        if (transaction is not null)
        {
            // Use the connection from the ambient transaction to avoid mismatch
            var connection =
                transaction.Connection
                ?? throw new InvalidOperationException("Transaction has no associated connection.");

            await using var command = new SqlCommand(sql, connection, transaction);
            AddMessageParameters(command, message);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Create a new connection when no ambient transaction exists
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
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
        var schema = string.IsNullOrWhiteSpace(_options.Schema) ? "dbo" : _options.Schema;
        var sql = $"[{schema}].[usp_GetPendingOutboxMessages]";

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandType = CommandType.StoredProcedure };

        _ = command.Parameters.AddWithValue("@batchSize", batchSize);

        return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var schema = string.IsNullOrWhiteSpace(_options.Schema) ? "dbo" : _options.Schema;
        var sql = $"[{schema}].[usp_GetFailedOutboxMessagesForRetry]";

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandType = CommandType.StoredProcedure };

        _ = command.Parameters.AddWithValue("@maxRetryCount", maxRetryCount);
        _ = command.Parameters.AddWithValue("@batchSize", batchSize);

        return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var schema = string.IsNullOrWhiteSpace(_options.Schema) ? "dbo" : _options.Schema;
        var sql = $"[{schema}].[usp_MarkOutboxMessageCompleted]";

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandType = CommandType.StoredProcedure };

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
        var schema = string.IsNullOrWhiteSpace(_options.Schema) ? "dbo" : _options.Schema;
        var sql = $"[{schema}].[usp_MarkOutboxMessageFailed]";

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandType = CommandType.StoredProcedure };

        _ = command.Parameters.AddWithValue("@messageId", messageId);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsDeadLetterAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var schema = string.IsNullOrWhiteSpace(_options.Schema) ? "dbo" : _options.Schema;
        var sql = $"[{schema}].[usp_MarkOutboxMessageDeadLetter]";

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandType = CommandType.StoredProcedure };

        _ = command.Parameters.AddWithValue("@messageId", messageId);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var schema = string.IsNullOrWhiteSpace(_options.Schema) ? "dbo" : _options.Schema;
        var sql = $"[{schema}].[usp_DeleteCompletedOutboxMessages]";

        var cutoffTime = _timeProvider.GetUtcNow().Subtract(olderThan);

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandType = CommandType.StoredProcedure };

        _ = command.Parameters.AddWithValue("@olderThanUtc", cutoffTime);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int count ? count : 0;
    }

    private async Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private SqlTransaction? GetCurrentTransaction() => _transactionScope?.GetCurrentTransaction() as SqlTransaction;

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
        _ = command.Parameters.AddWithValue("@RetryCount", message.RetryCount);
        _ = command.Parameters.AddWithValue("@Error", (object?)message.Error ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@Status", (int)message.Status);
    }

    private static async Task<IReadOnlyList<OutboxMessage>> ReadMessagesAsync(
        SqlCommand command,
        CancellationToken cancellationToken
    )
    {
        var messages = new List<OutboxMessage>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            messages.Add(MapToMessage(reader));
        }

        return messages;
    }

    private static OutboxMessage MapToMessage(SqlDataReader reader) =>
        new OutboxMessage
        {
            Id = reader.GetGuid(reader.GetOrdinal(OutboxMessageSchema.Columns.Id)),
            EventType = reader.GetString(reader.GetOrdinal(OutboxMessageSchema.Columns.EventType)),
            Payload = reader.GetString(reader.GetOrdinal(OutboxMessageSchema.Columns.Payload)),
            CorrelationId = reader.IsDBNull(reader.GetOrdinal(OutboxMessageSchema.Columns.CorrelationId))
                ? null
                : reader.GetString(reader.GetOrdinal(OutboxMessageSchema.Columns.CorrelationId)),
            CreatedAt = reader.GetDateTimeOffset(reader.GetOrdinal(OutboxMessageSchema.Columns.CreatedAt)),
            UpdatedAt = reader.GetDateTimeOffset(reader.GetOrdinal(OutboxMessageSchema.Columns.UpdatedAt)),
            ProcessedAt = reader.IsDBNull(reader.GetOrdinal(OutboxMessageSchema.Columns.ProcessedAt))
                ? null
                : reader.GetDateTimeOffset(reader.GetOrdinal(OutboxMessageSchema.Columns.ProcessedAt)),
            RetryCount = reader.GetInt32(reader.GetOrdinal(OutboxMessageSchema.Columns.RetryCount)),
            Error = reader.IsDBNull(reader.GetOrdinal(OutboxMessageSchema.Columns.Error))
                ? null
                : reader.GetString(reader.GetOrdinal(OutboxMessageSchema.Columns.Error)),
            Status = (OutboxMessageStatus)reader.GetInt32(reader.GetOrdinal(OutboxMessageSchema.Columns.Status)),
        };
}
