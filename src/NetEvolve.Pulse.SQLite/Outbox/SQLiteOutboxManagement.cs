namespace NetEvolve.Pulse.Outbox;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQLite implementation of <see cref="IOutboxManagement"/> using ADO.NET.
/// Provides dead-letter inspection, replay, and statistics queries.
/// </summary>
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
internal sealed class SQLiteOutboxManagement : IOutboxManagement
{
    /// <summary>The SQLite connection string resolved from <see cref="OutboxOptions"/>.</summary>
    private readonly string _connectionString;

    /// <summary>Whether to apply WAL journal mode on each opened connection.</summary>
    private readonly bool _enableWalMode;

    /// <summary>The time provider used to generate consistent timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    // Cached SQL statements
    private readonly string _getDeadLetterMessagesSql;
    private readonly string _getDeadLetterMessageSql;
    private readonly string _getDeadLetterCountSql;
    private readonly string _replayMessageSql;
    private readonly string _replayAllDeadLetterSql;
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteOutboxManagement"/> class.
    /// </summary>
    /// <param name="options">The SQLite outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public SQLiteOutboxManagement(IOptions<OutboxOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var opts = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(opts.ConnectionString);

        _connectionString = opts.ConnectionString;
        _enableWalMode = opts.EnableWalMode;
        _timeProvider = timeProvider;

        var table = opts.FullTableName;

        _getDeadLetterMessagesSql = $"""
            SELECT
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
                "{OutboxMessageSchema.Columns.Status}"
            FROM {table}
            WHERE "{OutboxMessageSchema.Columns.Status}" = 4
            ORDER BY "{OutboxMessageSchema.Columns.CreatedAt}" DESC
            LIMIT @pageSize OFFSET @offset;
            """;

        _getDeadLetterMessageSql = $"""
            SELECT
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
                "{OutboxMessageSchema.Columns.Status}"
            FROM {table}
            WHERE "{OutboxMessageSchema.Columns.Status}" = 4
              AND "{OutboxMessageSchema.Columns.Id}" = @messageId;
            """;

        _getDeadLetterCountSql = $"""
            SELECT COUNT(*)
            FROM {table}
            WHERE "{OutboxMessageSchema.Columns.Status}" = 4;
            """;

        _replayMessageSql = $"""
            UPDATE {table}
            SET "{OutboxMessageSchema.Columns.Status}" = 0,
                "{OutboxMessageSchema.Columns.UpdatedAt}" = @nowUtc,
                "{OutboxMessageSchema.Columns.Error}" = NULL,
                "{OutboxMessageSchema.Columns.ProcessedAt}" = NULL,
                "{OutboxMessageSchema.Columns.NextRetryAt}" = NULL,
                "{OutboxMessageSchema.Columns.RetryCount}" = 0
            WHERE "{OutboxMessageSchema.Columns.Id}" = @messageId
              AND "{OutboxMessageSchema.Columns.Status}" = 4;
            """;

        _replayAllDeadLetterSql = $"""
            UPDATE {table}
            SET "{OutboxMessageSchema.Columns.Status}" = 0,
                "{OutboxMessageSchema.Columns.UpdatedAt}" = @nowUtc,
                "{OutboxMessageSchema.Columns.Error}" = NULL,
                "{OutboxMessageSchema.Columns.ProcessedAt}" = NULL,
                "{OutboxMessageSchema.Columns.NextRetryAt}" = NULL,
                "{OutboxMessageSchema.Columns.RetryCount}" = 0
            WHERE "{OutboxMessageSchema.Columns.Status}" = 4;
            """;

        _getStatisticsSql = $"""
            SELECT
                SUM(CASE WHEN "{OutboxMessageSchema.Columns.Status}" = 0 THEN 1 ELSE 0 END) AS "{nameof(
                OutboxMessageStatus.Pending
            )}",
                SUM(CASE WHEN "{OutboxMessageSchema.Columns.Status}" = 1 THEN 1 ELSE 0 END) AS "{nameof(
                OutboxMessageStatus.Processing
            )}",
                SUM(CASE WHEN "{OutboxMessageSchema.Columns.Status}" = 2 THEN 1 ELSE 0 END) AS "{nameof(
                OutboxMessageStatus.Completed
            )}",
                SUM(CASE WHEN "{OutboxMessageSchema.Columns.Status}" = 3 THEN 1 ELSE 0 END) AS "{nameof(
                OutboxMessageStatus.Failed
            )}",
                SUM(CASE WHEN "{OutboxMessageSchema.Columns.Status}" = 4 THEN 1 ELSE 0 END) AS "{nameof(
                OutboxMessageStatus.DeadLetter
            )}"
            FROM {table};
            """;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetDeadLetterMessagesAsync(
        int pageSize = 50,
        int page = 0,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(page);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = new SqliteCommand(_getDeadLetterMessagesSql, connection);

            _ = command.Parameters.AddWithValue("@pageSize", pageSize);
            _ = command.Parameters.AddWithValue("@offset", page * pageSize);

            return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetDeadLetterMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = new SqliteCommand(_getDeadLetterMessageSql, connection);

            _ = command.Parameters.AddWithValue("@messageId", messageId.ToString());

            var messages = await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
            return messages.Count > 0 ? messages[0] : null;
        }
    }

    /// <inheritdoc />
    public async Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = new SqliteCommand(_getDeadLetterCountSql, connection);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is long count
                ? count
                : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReplayMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(_replayMessageSql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToString());
        _ = command.Parameters.AddWithValue("@nowUtc", now);

        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<int> ReplayAllDeadLetterAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(_replayAllDeadLetterSql, connection);

        _ = command.Parameters.AddWithValue("@nowUtc", now);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqliteCommand(_getStatisticsSql, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new OutboxStatistics();
        }

        var ordPending = reader.GetOrdinal(nameof(OutboxMessageStatus.Pending));
        var ordProcessing = reader.GetOrdinal(nameof(OutboxMessageStatus.Processing));
        var ordCompleted = reader.GetOrdinal(nameof(OutboxMessageStatus.Completed));
        var ordFailed = reader.GetOrdinal(nameof(OutboxMessageStatus.Failed));
        var ordDeadLetter = reader.GetOrdinal(nameof(OutboxMessageStatus.DeadLetter));

        var pendingNull = await reader.IsDBNullAsync(ordPending, cancellationToken).ConfigureAwait(false);
        var processingNull = await reader.IsDBNullAsync(ordProcessing, cancellationToken).ConfigureAwait(false);
        var completedNull = await reader.IsDBNullAsync(ordCompleted, cancellationToken).ConfigureAwait(false);
        var failedNull = await reader.IsDBNullAsync(ordFailed, cancellationToken).ConfigureAwait(false);
        var deadLetterNull = await reader.IsDBNullAsync(ordDeadLetter, cancellationToken).ConfigureAwait(false);

        return new OutboxStatistics
        {
            Pending = pendingNull ? 0L : reader.GetInt64(ordPending),
            Processing = processingNull ? 0L : reader.GetInt64(ordProcessing),
            Completed = completedNull ? 0L : reader.GetInt64(ordCompleted),
            Failed = failedNull ? 0L : reader.GetInt64(ordFailed),
            DeadLetter = deadLetterNull ? 0L : reader.GetInt64(ordDeadLetter),
        };
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
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="OutboxMessage"/> instances.
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
