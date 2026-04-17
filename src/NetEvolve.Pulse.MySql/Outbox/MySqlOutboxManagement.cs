namespace NetEvolve.Pulse.Outbox;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// MySQL implementation of <see cref="IOutboxManagement"/> using ADO.NET.
/// Provides dead-letter inspection, replay, and statistics queries.
/// </summary>
/// <remarks>
/// <para><strong>Schema:</strong></para>
/// MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
/// All tables reside in the active database specified by the connection string.
/// The <see cref="OutboxOptions.Schema"/> property is ignored for MySQL.
/// <para><strong>Data types:</strong></para>
/// <see cref="Guid"/> is stored as <c>BINARY(16)</c> and <see cref="DateTimeOffset"/>
/// as <c>BIGINT</c> (UTC ticks). All reads and writes apply the corresponding conversions.
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
internal sealed class MySqlOutboxManagement : IOutboxManagement
{
    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;

    // Cached SQL statements
    private readonly string _getDeadLetterMessagesSql;
    private readonly string _getDeadLetterMessageSql;
    private readonly string _getDeadLetterCountSql;
    private readonly string _replayMessageSql;
    private readonly string _replayAllDeadLetterSql;
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlOutboxManagement"/> class.
    /// </summary>
    /// <param name="options">The outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public MySqlOutboxManagement(IOptions<OutboxOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        _connectionString = options.Value.ConnectionString;
        _timeProvider = timeProvider;

        var table = options.Value.FullTableName;

        _getDeadLetterMessagesSql = $"""
            SELECT
                `{OutboxMessageSchema.Columns.Id}`,
                `{OutboxMessageSchema.Columns.EventType}`,
                `{OutboxMessageSchema.Columns.Payload}`,
                `{OutboxMessageSchema.Columns.CorrelationId}`,
                `{OutboxMessageSchema.Columns.CausationId}`,
                `{OutboxMessageSchema.Columns.CreatedAt}`,
                `{OutboxMessageSchema.Columns.UpdatedAt}`,
                `{OutboxMessageSchema.Columns.ProcessedAt}`,
                `{OutboxMessageSchema.Columns.NextRetryAt}`,
                `{OutboxMessageSchema.Columns.RetryCount}`,
                `{OutboxMessageSchema.Columns.Error}`,
                `{OutboxMessageSchema.Columns.Status}`
            FROM {table}
            WHERE `{OutboxMessageSchema.Columns.Status}` = 4
            ORDER BY `{OutboxMessageSchema.Columns.CreatedAt}` DESC
            LIMIT @pageSize OFFSET @offset
            """;

        _getDeadLetterMessageSql = $"""
            SELECT
                `{OutboxMessageSchema.Columns.Id}`,
                `{OutboxMessageSchema.Columns.EventType}`,
                `{OutboxMessageSchema.Columns.Payload}`,
                `{OutboxMessageSchema.Columns.CorrelationId}`,
                `{OutboxMessageSchema.Columns.CausationId}`,
                `{OutboxMessageSchema.Columns.CreatedAt}`,
                `{OutboxMessageSchema.Columns.UpdatedAt}`,
                `{OutboxMessageSchema.Columns.ProcessedAt}`,
                `{OutboxMessageSchema.Columns.NextRetryAt}`,
                `{OutboxMessageSchema.Columns.RetryCount}`,
                `{OutboxMessageSchema.Columns.Error}`,
                `{OutboxMessageSchema.Columns.Status}`
            FROM {table}
            WHERE `{OutboxMessageSchema.Columns.Status}` = 4
              AND `{OutboxMessageSchema.Columns.Id}` = @messageId
            """;

        _getDeadLetterCountSql = $"""
            SELECT COUNT(*)
            FROM {table}
            WHERE `{OutboxMessageSchema.Columns.Status}` = 4
            """;

        _replayMessageSql = $"""
            UPDATE {table}
            SET `{OutboxMessageSchema.Columns.Status}` = 0,
                `{OutboxMessageSchema.Columns.UpdatedAt}` = @nowTicks,
                `{OutboxMessageSchema.Columns.Error}` = NULL,
                `{OutboxMessageSchema.Columns.ProcessedAt}` = NULL,
                `{OutboxMessageSchema.Columns.NextRetryAt}` = NULL,
                `{OutboxMessageSchema.Columns.RetryCount}` = 0
            WHERE `{OutboxMessageSchema.Columns.Id}` = @messageId
              AND `{OutboxMessageSchema.Columns.Status}` = 4
            """;

        _replayAllDeadLetterSql = $"""
            UPDATE {table}
            SET `{OutboxMessageSchema.Columns.Status}` = 0,
                `{OutboxMessageSchema.Columns.UpdatedAt}` = @nowTicks,
                `{OutboxMessageSchema.Columns.Error}` = NULL,
                `{OutboxMessageSchema.Columns.ProcessedAt}` = NULL,
                `{OutboxMessageSchema.Columns.NextRetryAt}` = NULL,
                `{OutboxMessageSchema.Columns.RetryCount}` = 0
            WHERE `{OutboxMessageSchema.Columns.Status}` = 4
            """;

        _getStatisticsSql = $"""
            SELECT
                SUM(CASE WHEN `{OutboxMessageSchema.Columns.Status}` = 0 THEN 1 ELSE 0 END) AS `{nameof(
                OutboxMessageStatus.Pending
            )}`,
                SUM(CASE WHEN `{OutboxMessageSchema.Columns.Status}` = 1 THEN 1 ELSE 0 END) AS `{nameof(
                OutboxMessageStatus.Processing
            )}`,
                SUM(CASE WHEN `{OutboxMessageSchema.Columns.Status}` = 2 THEN 1 ELSE 0 END) AS `{nameof(
                OutboxMessageStatus.Completed
            )}`,
                SUM(CASE WHEN `{OutboxMessageSchema.Columns.Status}` = 3 THEN 1 ELSE 0 END) AS `{nameof(
                OutboxMessageStatus.Failed
            )}`,
                SUM(CASE WHEN `{OutboxMessageSchema.Columns.Status}` = 4 THEN 1 ELSE 0 END) AS `{nameof(
                OutboxMessageStatus.DeadLetter
            )}`
            FROM {table}
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

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_getDeadLetterMessagesSql, connection);

        _ = command.Parameters.AddWithValue("@pageSize", pageSize);
        _ = command.Parameters.AddWithValue("@offset", page * pageSize);

        return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetDeadLetterMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_getDeadLetterMessageSql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToByteArray());

        var messages = await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
        return messages.Count > 0 ? messages[0] : null;
    }

    /// <inheritdoc />
    public async Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_getDeadLetterCountSql, connection);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count
            ? count
            : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<bool> ReplayMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_replayMessageSql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToByteArray());
        _ = command.Parameters.AddWithValue("@nowTicks", nowTicks);

        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<int> ReplayAllDeadLetterAsync(CancellationToken cancellationToken = default)
    {
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_replayAllDeadLetterSql, connection);

        _ = command.Parameters.AddWithValue("@nowTicks", nowTicks);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_getStatisticsSql, connection);

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
            Pending = pendingNull
                ? 0L
                : Convert.ToInt64(reader.GetValue(ordPending), System.Globalization.CultureInfo.InvariantCulture),
            Processing = processingNull
                ? 0L
                : Convert.ToInt64(reader.GetValue(ordProcessing), System.Globalization.CultureInfo.InvariantCulture),
            Completed = completedNull
                ? 0L
                : Convert.ToInt64(reader.GetValue(ordCompleted), System.Globalization.CultureInfo.InvariantCulture),
            Failed = failedNull
                ? 0L
                : Convert.ToInt64(reader.GetValue(ordFailed), System.Globalization.CultureInfo.InvariantCulture),
            DeadLetter = deadLetterNull
                ? 0L
                : Convert.ToInt64(reader.GetValue(ordDeadLetter), System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Opens and returns a new <see cref="MySqlConnection"/> using the stored connection string.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    private async Task<MySqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="OutboxMessage"/> instances.
    /// Column ordinals are resolved once per result set to avoid repeated string lookups on every row.
    /// </summary>
    private static async Task<IReadOnlyList<OutboxMessage>> ReadMessagesAsync(
        MySqlCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

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
            var idBytes = await reader.GetFieldValueAsync<byte[]>(ordId, cancellationToken).ConfigureAwait(false);
            var correlationIdNull = await reader
                .IsDBNullAsync(ordCorrelationId, cancellationToken)
                .ConfigureAwait(false);
            var causationIdNull = await reader.IsDBNullAsync(ordCausationId, cancellationToken).ConfigureAwait(false);
            var processedAtNull = await reader.IsDBNullAsync(ordProcessedAt, cancellationToken).ConfigureAwait(false);
            var nextRetryAtNull = await reader.IsDBNullAsync(ordNextRetryAt, cancellationToken).ConfigureAwait(false);
            var errorNull = await reader.IsDBNullAsync(ordError, cancellationToken).ConfigureAwait(false);

            messages.Add(
                new OutboxMessage
                {
                    Id = new Guid(idBytes),
                    EventType =
                        Type.GetType(reader.GetString(ordEventType))
                        ?? throw new InvalidOperationException(
                            $"Cannot resolve event type '{reader.GetString(ordEventType)}'."
                        ),
                    Payload = reader.GetString(ordPayload),
                    CorrelationId = correlationIdNull ? null : reader.GetString(ordCorrelationId),
                    CausationId = causationIdNull ? null : reader.GetString(ordCausationId),
                    CreatedAt = new DateTimeOffset(reader.GetInt64(ordCreatedAt), TimeSpan.Zero),
                    UpdatedAt = new DateTimeOffset(reader.GetInt64(ordUpdatedAt), TimeSpan.Zero),
                    ProcessedAt = processedAtNull
                        ? null
                        : new DateTimeOffset(reader.GetInt64(ordProcessedAt), TimeSpan.Zero),
                    NextRetryAt = nextRetryAtNull
                        ? null
                        : new DateTimeOffset(reader.GetInt64(ordNextRetryAt), TimeSpan.Zero),
                    RetryCount = reader.GetInt32(ordRetryCount),
                    Error = errorNull ? null : reader.GetString(ordError),
                    Status = (OutboxMessageStatus)reader.GetInt32(ordStatus),
                }
            );
        } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));

        return messages;
    }
}
