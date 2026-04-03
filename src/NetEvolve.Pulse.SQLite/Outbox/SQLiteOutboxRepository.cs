namespace NetEvolve.Pulse.Outbox;

using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQLite implementation of <see cref="IOutboxRepository"/> using ADO.NET.
/// Provides optimized SQL operations with WAL mode support and <c>BEGIN IMMEDIATE</c> transactions
/// to prevent concurrent duplicate pickup in multi-threaded scenarios.
/// </summary>
/// <remarks>
/// <para><strong>Transaction Support:</strong></para>
/// <see cref="AddAsync"/> can participate in ambient transactions via <see cref="IOutboxTransactionScope"/>
/// or by using the connection with an active transaction.
/// <para><strong>Concurrency:</strong></para>
/// Uses <c>BEGIN IMMEDIATE</c> transactions (via <see cref="IsolationLevel.RepeatableRead"/>) during
/// pending-message claims to prevent duplicate processing by concurrent workers.
/// <para><strong>WAL Mode:</strong></para>
/// When <see cref="OutboxOptions.EnableWalMode"/> is <see langword="true"/>, the
/// <c>PRAGMA journal_mode=WAL</c> command is applied on each connection to allow concurrent
/// read access during writes.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is constructed from validated OutboxOptions.TableName property, not user input."
)]
[SuppressMessage(
    "Roslynator",
    "RCS1084:Use coalesce expression instead of conditional expression",
    Justification = "NextRetryAt and ProcessedAt properties require explicit conditional checks."
)]
internal sealed class SQLiteOutboxRepository : IOutboxRepository
{
    /// <summary>The SQLite connection string resolved from <see cref="OutboxOptions"/>.</summary>
    private readonly string _connectionString;

    /// <summary>Whether to apply WAL journal mode on each opened connection.</summary>
    private readonly bool _enableWalMode;

    /// <summary>The optional transaction scope providing an ambient <see cref="SqliteTransaction"/> for <see cref="AddAsync"/>.</summary>
    private readonly IOutboxTransactionScope? _transactionScope;

    /// <summary>The time provider used to generate consistent timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    // Cached SQL statements
    private readonly string _getPendingSql;
    private readonly string _getFailedForRetrySql;
    private readonly string _markCompletedSql;
    private readonly string _markFailedSql;
    private readonly string _markFailedWithRetrySql;
    private readonly string _markDeadLetterSql;
    private readonly string _deleteCompletedSql;
    private readonly string _getPendingCountSql;
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteOutboxRepository"/> class.
    /// </summary>
    /// <param name="options">The SQLite outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    /// <param name="transactionScope">Optional transaction scope for ambient transaction support.</param>
    public SQLiteOutboxRepository(
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        IOutboxTransactionScope? transactionScope = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var opts = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(opts.ConnectionString);

        _connectionString = opts.ConnectionString;
        _enableWalMode = opts.EnableWalMode;
        _timeProvider = timeProvider;
        _transactionScope = transactionScope;

        var table = opts.FullTableName;

        _getPendingSql = $"""
            UPDATE {table}
            SET "{OutboxMessageSchema.Columns.Status}" = 1,
                "{OutboxMessageSchema.Columns.UpdatedAt}" = @nowUtc
            WHERE "{OutboxMessageSchema.Columns.Id}" IN (
                SELECT "{OutboxMessageSchema.Columns.Id}"
                FROM {table}
                WHERE "{OutboxMessageSchema.Columns.Status}" = 0
                  AND ("{OutboxMessageSchema.Columns.NextRetryAt}" IS NULL
                       OR "{OutboxMessageSchema.Columns.NextRetryAt}" <= @nowUtc)
                ORDER BY "{OutboxMessageSchema.Columns.CreatedAt}"
                LIMIT @batchSize
            )
            RETURNING
                "{OutboxMessageSchema.Columns.Id}",
                "{OutboxMessageSchema.Columns.EventType}",
                "{OutboxMessageSchema.Columns.Payload}",
                "{OutboxMessageSchema.Columns.CorrelationId}",
                "{OutboxMessageSchema.Columns.CreatedAt}",
                "{OutboxMessageSchema.Columns.UpdatedAt}",
                "{OutboxMessageSchema.Columns.ProcessedAt}",
                "{OutboxMessageSchema.Columns.NextRetryAt}",
                "{OutboxMessageSchema.Columns.RetryCount}",
                "{OutboxMessageSchema.Columns.Error}",
                "{OutboxMessageSchema.Columns.Status}";
            """;

        _getFailedForRetrySql = $"""
            UPDATE {table}
            SET "{OutboxMessageSchema.Columns.Status}" = 1,
                "{OutboxMessageSchema.Columns.UpdatedAt}" = @nowUtc
            WHERE "{OutboxMessageSchema.Columns.Id}" IN (
                SELECT "{OutboxMessageSchema.Columns.Id}"
                FROM {table}
                WHERE "{OutboxMessageSchema.Columns.Status}" = 3
                  AND "{OutboxMessageSchema.Columns.RetryCount}" < @maxRetryCount
                  AND ("{OutboxMessageSchema.Columns.NextRetryAt}" IS NULL
                       OR "{OutboxMessageSchema.Columns.NextRetryAt}" <= @nowUtc)
                ORDER BY "{OutboxMessageSchema.Columns.CreatedAt}"
                LIMIT @batchSize
            )
            RETURNING
                "{OutboxMessageSchema.Columns.Id}",
                "{OutboxMessageSchema.Columns.EventType}",
                "{OutboxMessageSchema.Columns.Payload}",
                "{OutboxMessageSchema.Columns.CorrelationId}",
                "{OutboxMessageSchema.Columns.CreatedAt}",
                "{OutboxMessageSchema.Columns.UpdatedAt}",
                "{OutboxMessageSchema.Columns.ProcessedAt}",
                "{OutboxMessageSchema.Columns.NextRetryAt}",
                "{OutboxMessageSchema.Columns.RetryCount}",
                "{OutboxMessageSchema.Columns.Error}",
                "{OutboxMessageSchema.Columns.Status}";
            """;

        _markCompletedSql = $"""
            UPDATE {table}
            SET "{OutboxMessageSchema.Columns.Status}" = 2,
                "{OutboxMessageSchema.Columns.UpdatedAt}" = @nowUtc,
                "{OutboxMessageSchema.Columns.ProcessedAt}" = @nowUtc
            WHERE "{OutboxMessageSchema.Columns.Id}" = @messageId;
            """;

        _markFailedSql = $"""
            UPDATE {table}
            SET "{OutboxMessageSchema.Columns.Status}" = 3,
                "{OutboxMessageSchema.Columns.UpdatedAt}" = @nowUtc,
                "{OutboxMessageSchema.Columns.Error}" = @error,
                "{OutboxMessageSchema.Columns.RetryCount}" = "{OutboxMessageSchema.Columns.RetryCount}" + 1
            WHERE "{OutboxMessageSchema.Columns.Id}" = @messageId;
            """;

        _markFailedWithRetrySql = $"""
            UPDATE {table}
            SET "{OutboxMessageSchema.Columns.Status}" = 3,
                "{OutboxMessageSchema.Columns.UpdatedAt}" = @nowUtc,
                "{OutboxMessageSchema.Columns.Error}" = @error,
                "{OutboxMessageSchema.Columns.RetryCount}" = "{OutboxMessageSchema.Columns.RetryCount}" + 1,
                "{OutboxMessageSchema.Columns.NextRetryAt}" = @nextRetryAt
            WHERE "{OutboxMessageSchema.Columns.Id}" = @messageId;
            """;

        _markDeadLetterSql = $"""
            UPDATE {table}
            SET "{OutboxMessageSchema.Columns.Status}" = 4,
                "{OutboxMessageSchema.Columns.UpdatedAt}" = @nowUtc,
                "{OutboxMessageSchema.Columns.ProcessedAt}" = @nowUtc,
                "{OutboxMessageSchema.Columns.Error}" = @error
            WHERE "{OutboxMessageSchema.Columns.Id}" = @messageId;
            """;

        _deleteCompletedSql = $"""
            DELETE FROM {table}
            WHERE "{OutboxMessageSchema.Columns.Status}" = 2
              AND "{OutboxMessageSchema.Columns.ProcessedAt}" <= @olderThanUtc;
            """;

        _getPendingCountSql = $"""
            SELECT COUNT(*)
            FROM {table}
            WHERE "{OutboxMessageSchema.Columns.Status}" = 0;
            """;

        _insertSql = $"""
            INSERT INTO {table}
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
                (@Id, @EventType, @Payload, @CorrelationId, @CreatedAt, @UpdatedAt,
                 @ProcessedAt, @NextRetryAt, @RetryCount, @Error, @Status);
            """;
    }

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var transaction = GetCurrentTransaction();

        if (transaction is not null)
        {
            var connection =
                transaction.Connection
                ?? throw new InvalidOperationException("Transaction has no associated connection.");

            await using var command = new SqliteCommand(_insertSql, connection, transaction);
            AddMessageParameters(command, message);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqliteCommand(_insertSql, connection);
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

        // BEGIN IMMEDIATE prevents concurrent duplicate pickup (IsolationLevel.RepeatableRead maps to BEGIN IMMEDIATE in SQLite)
        await using var transaction = (SqliteTransaction)
            await connection
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
                .ConfigureAwait(false);

        try
        {
            await using var command = new SqliteCommand(_getPendingSql, connection, transaction);
            _ = command.Parameters.AddWithValue("@batchSize", batchSize);
            _ = command.Parameters.AddWithValue("@nowUtc", now);

            var messages = await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return messages;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
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

        // BEGIN IMMEDIATE prevents concurrent duplicate pickup (IsolationLevel.RepeatableRead maps to BEGIN IMMEDIATE in SQLite)
        await using var transaction = (SqliteTransaction)
            await connection
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
                .ConfigureAwait(false);

        try
        {
            await using var command = new SqliteCommand(_getFailedForRetrySql, connection, transaction);
            _ = command.Parameters.AddWithValue("@maxRetryCount", maxRetryCount);
            _ = command.Parameters.AddWithValue("@batchSize", batchSize);
            _ = command.Parameters.AddWithValue("@nowUtc", now);

            var messages = await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return messages;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(_markCompletedSql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToString());
        _ = command.Parameters.AddWithValue("@nowUtc", now);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(_markFailedSql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToString());
        _ = command.Parameters.AddWithValue("@nowUtc", now);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);

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
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(_markFailedWithRetrySql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToString());
        _ = command.Parameters.AddWithValue("@nowUtc", now);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);
        _ = command.Parameters.AddWithValue(
            "@nextRetryAt",
            nextRetryAt.HasValue ? (object)nextRetryAt.Value : DBNull.Value
        );

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsDeadLetterAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(_markDeadLetterSql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToString());
        _ = command.Parameters.AddWithValue("@nowUtc", now);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(_getPendingCountSql, connection);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count
            ? count
            : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoffTime = _timeProvider.GetUtcNow().Subtract(olderThan);

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(_deleteCompletedSql, connection);

        _ = command.Parameters.AddWithValue("@olderThanUtc", cutoffTime);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens and returns a new <see cref="SqliteConnection"/> using the stored connection string.
    /// Applies WAL mode when <see cref="OutboxOptions.EnableWalMode"/> is <see langword="true"/>.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An open <see cref="SqliteConnection"/>.</returns>
    private async Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (_enableWalMode)
        {
            await using var walCmd = new SqliteCommand("PRAGMA journal_mode=WAL;", connection);
            _ = await walCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }

    /// <summary>
    /// Retrieves the current ambient <see cref="SqliteTransaction"/> from the registered
    /// <see cref="IOutboxTransactionScope"/>, or <see langword="null"/> if no scope is configured
    /// or no transaction is active.
    /// </summary>
    /// <returns>The current <see cref="SqliteTransaction"/>, or <see langword="null"/>.</returns>
    private SqliteTransaction? GetCurrentTransaction() =>
        _transactionScope?.GetCurrentTransaction() as SqliteTransaction;

    /// <summary>
    /// Adds all <see cref="OutboxMessage"/> property values as parameters to a <see cref="SqliteCommand"/>.
    /// </summary>
    /// <param name="command">The command to which parameters are added.</param>
    /// <param name="message">The outbox message providing parameter values.</param>
    private static void AddMessageParameters(SqliteCommand command, OutboxMessage message)
    {
        _ = command.Parameters.AddWithValue("@Id", message.Id.ToString());
        _ = command.Parameters.AddWithValue("@EventType", message.EventType.ToOutboxEventTypeName());
        _ = command.Parameters.AddWithValue("@Payload", message.Payload);
        _ = command.Parameters.AddWithValue("@CorrelationId", (object?)message.CorrelationId ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@CreatedAt", message.CreatedAt);
        _ = command.Parameters.AddWithValue("@UpdatedAt", message.UpdatedAt);
        _ = command.Parameters.AddWithValue(
            "@ProcessedAt",
            message.ProcessedAt.HasValue ? (object)message.ProcessedAt.Value : DBNull.Value
        );
        _ = command.Parameters.AddWithValue(
            "@NextRetryAt",
            message.NextRetryAt.HasValue ? (object)message.NextRetryAt.Value : DBNull.Value
        );
        _ = command.Parameters.AddWithValue("@RetryCount", message.RetryCount);
        _ = command.Parameters.AddWithValue("@Error", (object?)message.Error ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@Status", (int)message.Status);
    }

    /// <summary>
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="OutboxMessage"/> instances.
    /// Column ordinals are resolved once per result set to avoid repeated string lookups on every row.
    /// </summary>
    /// <param name="command">The <see cref="SqliteCommand"/> to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of <see cref="OutboxMessage"/> records.</returns>
    private static async Task<IReadOnlyList<OutboxMessage>> ReadMessagesAsync(
        SqliteCommand command,
        CancellationToken cancellationToken
    )
    {
        var messages = new List<OutboxMessage>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return messages;
        }

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
            var correlationIdNull = await reader
                .IsDBNullAsync(ordCorrelationId, cancellationToken)
                .ConfigureAwait(false);
            var processedAtNull = await reader.IsDBNullAsync(ordProcessedAt, cancellationToken).ConfigureAwait(false);
            var nextRetryAtNull = await reader.IsDBNullAsync(ordNextRetryAt, cancellationToken).ConfigureAwait(false);
            var errorNull = await reader.IsDBNullAsync(ordError, cancellationToken).ConfigureAwait(false);
            var createdAt = await reader
                .GetFieldValueAsync<DateTimeOffset>(ordCreatedAt, cancellationToken)
                .ConfigureAwait(false);
            var updatedAt = await reader
                .GetFieldValueAsync<DateTimeOffset>(ordUpdatedAt, cancellationToken)
                .ConfigureAwait(false);
            var processedAt = processedAtNull
                ? (DateTimeOffset?)null
                : await reader
                    .GetFieldValueAsync<DateTimeOffset>(ordProcessedAt, cancellationToken)
                    .ConfigureAwait(false);
            var nextRetryAt = nextRetryAtNull
                ? (DateTimeOffset?)null
                : await reader
                    .GetFieldValueAsync<DateTimeOffset>(ordNextRetryAt, cancellationToken)
                    .ConfigureAwait(false);

            messages.Add(
                new OutboxMessage
                {
                    Id = Guid.Parse(reader.GetString(ordId)),
                    EventType =
                        Type.GetType(reader.GetString(ordEventType))
                        ?? throw new InvalidOperationException(
                            $"Cannot resolve event type '{reader.GetString(ordEventType)}'."
                        ),
                    Payload = reader.GetString(ordPayload),
                    CorrelationId = correlationIdNull ? null : reader.GetString(ordCorrelationId),
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    ProcessedAt = processedAt,
                    NextRetryAt = nextRetryAt,
                    RetryCount = (int)reader.GetInt64(ordRetryCount),
                    Error = errorNull ? null : reader.GetString(ordError),
                    Status = (OutboxMessageStatus)reader.GetInt64(ordStatus),
                }
            );
        } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));

        return messages;
    }
}
