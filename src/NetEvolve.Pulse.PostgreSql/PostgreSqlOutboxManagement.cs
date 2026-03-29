namespace NetEvolve.Pulse;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of <see cref="IOutboxManagement"/> using ADO.NET.
/// Provides dead-letter inspection, replay, and statistics queries via optimized PostgreSQL functions.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/OutboxMessage.sql</c> to create the required
/// functions before using this provider.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Function names are constructed from validated OutboxOptions.Schema property, not user input."
)]
internal sealed class PostgreSqlOutboxManagement : IOutboxManagement
{
    /// <summary>The PostgreSQL connection string used to open new connections for each operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached SQL for calling the get_dead_letter_outbox_messages function.</summary>
    private readonly string _getDeadLetterMessagesSql;

    /// <summary>Cached SQL for calling the get_dead_letter_outbox_message function.</summary>
    private readonly string _getDeadLetterMessageSql;

    /// <summary>Cached SQL for calling the get_dead_letter_outbox_message_count function.</summary>
    private readonly string _getDeadLetterCountSql;

    /// <summary>Cached SQL for calling the replay_outbox_message function.</summary>
    private readonly string _replayMessageSql;

    /// <summary>Cached SQL for calling the replay_all_dead_letter_outbox_messages function.</summary>
    private readonly string _replayAllDeadLetterSql;

    /// <summary>Cached SQL for calling the get_outbox_statistics function.</summary>
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxManagement"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="options">The outbox configuration options.</param>
    public PostgreSqlOutboxManagement(string connectionString, IOptions<OutboxOptions> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        _connectionString = connectionString;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? OutboxMessageSchema.DefaultSchema
            : options.Value.Schema;
        _getDeadLetterMessagesSql = $"SELECT * FROM \"{schema}\".get_dead_letter_outbox_messages(@page_size, @page)";
        _getDeadLetterMessageSql = $"SELECT * FROM \"{schema}\".get_dead_letter_outbox_message(@message_id)";
        _getDeadLetterCountSql = $"SELECT \"{schema}\".get_dead_letter_outbox_message_count()";
        _replayMessageSql = $"SELECT \"{schema}\".replay_outbox_message(@message_id)";
        _replayAllDeadLetterSql = $"SELECT \"{schema}\".replay_all_dead_letter_outbox_messages()";
        _getStatisticsSql = $"SELECT * FROM \"{schema}\".get_outbox_statistics()";
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
        await using var command = new NpgsqlCommand(_getDeadLetterMessagesSql, connection);

        _ = command.Parameters.AddWithValue("page_size", pageSize);
#pragma warning disable RCS1015 // Use nameof operator
        _ = command.Parameters.AddWithValue("page", page);
#pragma warning restore RCS1015 // Use nameof operator

        return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetDeadLetterMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_getDeadLetterMessageSql, connection);

        _ = command.Parameters.AddWithValue("message_id", messageId);

        var messages = await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
        return messages.Count > 0 ? messages[0] : null;
    }

    /// <inheritdoc />
    public async Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_getDeadLetterCountSql, connection);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count
            ? count
            : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<bool> ReplayMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_replayMessageSql, connection);

        _ = command.Parameters.AddWithValue("message_id", messageId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var updated = result is int count
            ? count
            : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<int> ReplayAllDeadLetterAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_replayAllDeadLetterSql, connection);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int count ? count : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_getStatisticsSql, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new OutboxStatistics();
        }

        var ordPending = reader.GetOrdinal("Pending");
        var ordProcessing = reader.GetOrdinal("Processing");
        var ordCompleted = reader.GetOrdinal("Completed");
        var ordFailed = reader.GetOrdinal("Failed");
        var ordDeadLetter = reader.GetOrdinal("DeadLetter");

        return new OutboxStatistics
        {
            Pending = reader.GetInt64(ordPending),
            Processing = reader.GetInt64(ordProcessing),
            Completed = reader.GetInt64(ordCompleted),
            Failed = reader.GetInt64(ordFailed),
            DeadLetter = reader.GetInt64(ordDeadLetter),
        };
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
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="OutboxMessage"/> instances.
    /// Column ordinals are resolved once per result set to avoid repeated string lookups on every row.
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
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(ordCreatedAt),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(ordUpdatedAt),
            ProcessedAt = reader.IsDBNull(ordProcessedAt) ? null : reader.GetFieldValue<DateTimeOffset>(ordProcessedAt),
            NextRetryAt = reader.IsDBNull(ordNextRetryAt) ? null : reader.GetFieldValue<DateTimeOffset>(ordNextRetryAt),
            RetryCount = reader.GetInt32(ordRetryCount),
            Error = reader.IsDBNull(ordError) ? null : reader.GetString(ordError),
            Status = (OutboxMessageStatus)reader.GetInt32(ordStatus),
        };
}
