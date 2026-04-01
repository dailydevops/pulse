namespace NetEvolve.Pulse.Outbox;

using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQL Server implementation of <see cref="IOutboxManagement"/> using ADO.NET.
/// Provides dead-letter inspection, replay, and statistics queries via optimized T-SQL stored procedures.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/OutboxMessage.sql</c> to create the required
/// stored procedures before using this provider.
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
internal sealed class SqlServerOutboxManagement : IOutboxManagement
{
    /// <summary>The SQL Server connection string used to open new connections for each operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached stored procedure name for retrieving dead-letter messages (paginated).</summary>
    private readonly string _getDeadLetterMessagesSql;

    /// <summary>Cached stored procedure name for retrieving a single dead-letter message.</summary>
    private readonly string _getDeadLetterMessageSql;

    /// <summary>Cached stored procedure name for counting dead-letter messages.</summary>
    private readonly string _getDeadLetterCountSql;

    /// <summary>Cached stored procedure name for replaying a single dead-letter message.</summary>
    private readonly string _replayMessageSql;

    /// <summary>Cached stored procedure name for replaying all dead-letter messages.</summary>
    private readonly string _replayAllDeadLetterSql;

    /// <summary>Cached stored procedure name for retrieving outbox statistics.</summary>
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerOutboxManagement"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="options">The outbox configuration options.</param>
    public SqlServerOutboxManagement(string connectionString, IOptions<OutboxOptions> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        _connectionString = connectionString;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? OutboxMessageSchema.DefaultSchema
            : options.Value.Schema;
        _getDeadLetterMessagesSql = $"[{schema}].[usp_GetDeadLetterOutboxMessages]";
        _getDeadLetterMessageSql = $"[{schema}].[usp_GetDeadLetterOutboxMessage]";
        _getDeadLetterCountSql = $"[{schema}].[usp_GetDeadLetterOutboxMessageCount]";
        _replayMessageSql = $"[{schema}].[usp_ReplayOutboxMessage]";
        _replayAllDeadLetterSql = $"[{schema}].[usp_ReplayAllDeadLetterOutboxMessages]";
        _getStatisticsSql = $"[{schema}].[usp_GetOutboxStatistics]";
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
        await using var command = new SqlCommand(_getDeadLetterMessagesSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@pageSize", pageSize);
        _ = command.Parameters.AddWithValue("@page", page);

        return await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetDeadLetterMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_getDeadLetterMessageSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@messageId", messageId);

        var messages = await ReadMessagesAsync(command, cancellationToken).ConfigureAwait(false);
        return messages.Count > 0 ? messages[0] : null;
    }

    /// <inheritdoc />
    public async Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_getDeadLetterCountSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count
            ? count
            : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<bool> ReplayMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_replayMessageSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        _ = command.Parameters.AddWithValue("@messageId", messageId);

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
        await using var command = new SqlCommand(_replayAllDeadLetterSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int count ? count : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_getStatisticsSql, connection)
        {
            CommandType = CommandType.StoredProcedure,
        };

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
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="OutboxMessage"/> instances.
    /// Column ordinals are resolved once per result set to avoid repeated string lookups on every row.
    /// </summary>
    /// <param name="command">The <see cref="SqlCommand"/> to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of <see cref="OutboxMessage"/> records.</returns>
    private static async Task<IReadOnlyList<OutboxMessage>> ReadMessagesAsync(
        SqlCommand command,
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
            CreatedAt = reader.GetDateTimeOffset(ordCreatedAt),
            UpdatedAt = reader.GetDateTimeOffset(ordUpdatedAt),
            ProcessedAt = reader.IsDBNull(ordProcessedAt) ? null : reader.GetDateTimeOffset(ordProcessedAt),
            RetryCount = reader.GetInt32(ordRetryCount),
            Error = reader.IsDBNull(ordError) ? null : reader.GetString(ordError),
            Status = (OutboxMessageStatus)reader.GetInt32(ordStatus),
        };
}
