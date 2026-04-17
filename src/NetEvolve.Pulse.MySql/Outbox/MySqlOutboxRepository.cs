namespace NetEvolve.Pulse.Outbox;

using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// MySQL implementation of <see cref="IOutboxRepository"/> using ADO.NET.
/// Provides optimized MySQL operations with proper transaction and locking support.
/// </summary>
/// <remarks>
/// <para><strong>Requirements:</strong></para>
/// MySQL 8.0 or later is required for <c>SELECT … FOR UPDATE SKIP LOCKED</c> support.
/// <para><strong>Schema:</strong></para>
/// MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
/// All tables reside in the active database specified by the connection string.
/// The <see cref="OutboxOptions.Schema"/> property is ignored for MySQL.
/// <para><strong>Data types:</strong></para>
/// <list type="bullet">
///   <item><description><see cref="Guid"/> is stored as <c>BINARY(16)</c> using <see cref="Guid.ToByteArray()"/> — interchangeable with the Entity Framework MySQL provider.</description></item>
///   <item><description><see cref="DateTimeOffset"/> is stored as <c>BIGINT</c> (UTC ticks) — interchangeable with the Entity Framework MySQL provider.</description></item>
/// </list>
/// <para><strong>Concurrency:</strong></para>
/// <see cref="GetPendingAsync"/> and <see cref="GetFailedForRetryAsync"/> open an explicit
/// <c>REPEATABLE READ</c> transaction, issue a <c>SELECT … FOR UPDATE SKIP LOCKED</c> to
/// atomically claim a batch of rows, update their status to Processing, and then commit.
/// Concurrent workers skip locked rows and each receive a distinct batch.
/// <para><strong>Transaction Support:</strong></para>
/// <see cref="AddAsync"/> participates in ambient transactions via <see cref="IOutboxTransactionScope"/>
/// when one is registered; otherwise it opens its own connection.
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
internal sealed class MySqlOutboxRepository : IOutboxRepository
{
    /// <summary>The MySQL connection string used to open new connections for each repository operation.</summary>
    private readonly string _connectionString;

    /// <summary>The optional transaction scope providing an ambient <see cref="MySqlTransaction"/> for <see cref="AddAsync"/>.</summary>
    private readonly IOutboxTransactionScope? _transactionScope;

    /// <summary>The time provider used to generate consistent timestamps for cutoff calculations.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Cached backtick-quoted table name, e.g. <c>`OutboxMessage`</c>.</summary>
    // Cached SQL for simple single-row operations
    private readonly string _markCompletedSql;
    private readonly string _markFailedSql;
    private readonly string _markFailedWithRetrySql;
    private readonly string _markDeadLetterSql;
    private readonly string _deleteCompletedSql;
    private readonly string _getPendingCountSql;
    private readonly string _insertSql;

    // Cached SQL fragments for the two-phase pending/retry queries
    private readonly string _selectPendingIdsSql;
    private readonly string _selectFailedForRetryIdsSql;
    private readonly string _selectByIdsSqlTemplate;
    private readonly string _updateToProcessingSqlTemplate;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlOutboxRepository"/> class.
    /// </summary>
    /// <param name="options">The outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    /// <param name="transactionScope">Optional transaction scope for ambient transaction support.</param>
    public MySqlOutboxRepository(
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

        var tableName = options.Value.FullTableName;

        // Phase 1 of pending poll: SELECT IDs with row-level locking
        _selectPendingIdsSql = $"""
            SELECT `{OutboxMessageSchema.Columns.Id}`
            FROM {tableName}
            WHERE `{OutboxMessageSchema.Columns.Status}` = 0
              AND (`{OutboxMessageSchema.Columns.NextRetryAt}` IS NULL
                   OR `{OutboxMessageSchema.Columns.NextRetryAt}` <= @nowTicks)
            ORDER BY `{OutboxMessageSchema.Columns.CreatedAt}` ASC
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED
            """;

        // Phase 1 of retry poll: SELECT IDs with row-level locking
        _selectFailedForRetryIdsSql = $"""
            SELECT `{OutboxMessageSchema.Columns.Id}`
            FROM {tableName}
            WHERE `{OutboxMessageSchema.Columns.Status}` = 3
              AND `{OutboxMessageSchema.Columns.RetryCount}` < @maxRetryCount
              AND (`{OutboxMessageSchema.Columns.NextRetryAt}` IS NULL
                   OR `{OutboxMessageSchema.Columns.NextRetryAt}` <= @nowTicks)
            ORDER BY `{OutboxMessageSchema.Columns.CreatedAt}` ASC
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED
            """;

        // Phase 2 (UPDATE) and Phase 3 (SELECT) templates — {0} is replaced at call-time with the IN-param list.
        // Use $$"""...""" so that {{expr}} is the interpolation hole and bare {0} is a literal placeholder.
        _updateToProcessingSqlTemplate = $$"""
            UPDATE {{tableName}}
            SET `{{OutboxMessageSchema.Columns.Status}}` = 1,
                `{{OutboxMessageSchema.Columns.UpdatedAt}}` = @nowTicks
            WHERE `{{OutboxMessageSchema.Columns.Id}}` IN ({0})
            """;

        _selectByIdsSqlTemplate = $$"""
            SELECT
                `{{OutboxMessageSchema.Columns.Id}}`,
                `{{OutboxMessageSchema.Columns.EventType}}`,
                `{{OutboxMessageSchema.Columns.Payload}}`,
                `{{OutboxMessageSchema.Columns.CorrelationId}}`,
                `{{OutboxMessageSchema.Columns.CausationId}}`,
                `{{OutboxMessageSchema.Columns.CreatedAt}}`,
                `{{OutboxMessageSchema.Columns.UpdatedAt}}`,
                `{{OutboxMessageSchema.Columns.ProcessedAt}}`,
                `{{OutboxMessageSchema.Columns.NextRetryAt}}`,
                `{{OutboxMessageSchema.Columns.RetryCount}}`,
                `{{OutboxMessageSchema.Columns.Error}}`,
                `{{OutboxMessageSchema.Columns.Status}}`
            FROM {{tableName}}
            WHERE `{{OutboxMessageSchema.Columns.Id}}` IN ({0})
            """;

        _markCompletedSql = $"""
            UPDATE {tableName}
            SET `{OutboxMessageSchema.Columns.Status}` = 2,
                `{OutboxMessageSchema.Columns.UpdatedAt}` = @nowTicks,
                `{OutboxMessageSchema.Columns.ProcessedAt}` = @nowTicks
            WHERE `{OutboxMessageSchema.Columns.Id}` = @messageId
            """;

        _markFailedSql = $"""
            UPDATE {tableName}
            SET `{OutboxMessageSchema.Columns.Status}` = 3,
                `{OutboxMessageSchema.Columns.UpdatedAt}` = @nowTicks,
                `{OutboxMessageSchema.Columns.Error}` = @error,
                `{OutboxMessageSchema.Columns.RetryCount}` = `{OutboxMessageSchema.Columns.RetryCount}` + 1
            WHERE `{OutboxMessageSchema.Columns.Id}` = @messageId
            """;

        _markFailedWithRetrySql = $"""
            UPDATE {tableName}
            SET `{OutboxMessageSchema.Columns.Status}` = 3,
                `{OutboxMessageSchema.Columns.UpdatedAt}` = @nowTicks,
                `{OutboxMessageSchema.Columns.Error}` = @error,
                `{OutboxMessageSchema.Columns.RetryCount}` = `{OutboxMessageSchema.Columns.RetryCount}` + 1,
                `{OutboxMessageSchema.Columns.NextRetryAt}` = @nextRetryAtTicks
            WHERE `{OutboxMessageSchema.Columns.Id}` = @messageId
            """;

        _markDeadLetterSql = $"""
            UPDATE {tableName}
            SET `{OutboxMessageSchema.Columns.Status}` = 4,
                `{OutboxMessageSchema.Columns.UpdatedAt}` = @nowTicks,
                `{OutboxMessageSchema.Columns.ProcessedAt}` = @nowTicks,
                `{OutboxMessageSchema.Columns.Error}` = @error
            WHERE `{OutboxMessageSchema.Columns.Id}` = @messageId
            """;

        _deleteCompletedSql = $"""
            DELETE FROM {tableName}
            WHERE `{OutboxMessageSchema.Columns.Status}` = 2
              AND `{OutboxMessageSchema.Columns.ProcessedAt}` <= @olderThanTicks
            """;

        _getPendingCountSql = $"""
            SELECT COUNT(*)
            FROM {tableName}
            WHERE `{OutboxMessageSchema.Columns.Status}` = 0
            """;

        _insertSql = $"""
            INSERT INTO {tableName}
                (`{OutboxMessageSchema.Columns.Id}`,
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
                 `{OutboxMessageSchema.Columns.Status}`)
            VALUES
                (@Id, @EventType, @Payload, @CorrelationId, @CausationId, @CreatedAt, @UpdatedAt,
                 @ProcessedAt, @NextRetryAt, @RetryCount, @Error, @Status)
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

            await using var command = new MySqlCommand(_insertSql, connection, transaction);
            AddMessageParameters(command, message);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Create a new connection when no ambient transaction exists
            await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(_insertSql, connection);
            AddMessageParameters(command, message);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    ) =>
        await FetchAndClaimMessagesAsync(_selectPendingIdsSql, batchSize, null, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default
    ) =>
        await FetchAndClaimMessagesAsync(_selectFailedForRetryIdsSql, batchSize, maxRetryCount, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_getPendingCountSql, connection);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count
            ? count
            : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_markCompletedSql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToByteArray());
        _ = command.Parameters.AddWithValue("@nowTicks", nowTicks);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_markFailedSql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToByteArray());
        _ = command.Parameters.AddWithValue("@nowTicks", nowTicks);
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
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_markFailedWithRetrySql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToByteArray());
        _ = command.Parameters.AddWithValue("@nowTicks", nowTicks);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);
        _ = command.Parameters.AddWithValue(
            "@nextRetryAtTicks",
            nextRetryAt.HasValue ? (object)nextRetryAt.Value.UtcTicks : DBNull.Value
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
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_markDeadLetterSql, connection);

        _ = command.Parameters.AddWithValue("@messageId", messageId.ToByteArray());
        _ = command.Parameters.AddWithValue("@nowTicks", nowTicks);
        _ = command.Parameters.AddWithValue("@error", (object?)errorMessage ?? DBNull.Value);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoffTicks = _timeProvider.GetUtcNow().Subtract(olderThan).UtcTicks;

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_deleteCompletedSql, connection);

        _ = command.Parameters.AddWithValue("@olderThanTicks", cutoffTicks);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Implements the two-phase claim pattern for both pending and failed-for-retry polls.
    /// </summary>
    /// <remarks>
    /// The operation runs inside a <c>REPEATABLE READ</c> transaction:
    /// <list type="number">
    ///   <item>SELECT IDs with <c>FOR UPDATE SKIP LOCKED</c> — locks the rows exclusively.</item>
    ///   <item>UPDATE those rows to <c>Status = Processing</c> within the same transaction.</item>
    ///   <item>SELECT the full rows by those IDs.</item>
    ///   <item>COMMIT — releases the locks.</item>
    /// </list>
    /// Concurrent workers skip the locked rows and receive a distinct batch each.
    /// </remarks>
    /// <param name="selectIdsSql">The SQL that selects IDs with <c>FOR UPDATE SKIP LOCKED</c>.</param>
    /// <param name="batchSize">Maximum number of messages to claim.</param>
    /// <param name="maxRetryCount">When non-null, bound passed as <c>@maxRetryCount</c> for the failed-for-retry query.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The claimed and updated outbox messages.</returns>
    private async Task<IReadOnlyList<OutboxMessage>> FetchAndClaimMessagesAsync(
        string selectIdsSql,
        int batchSize,
        int? maxRetryCount,
        CancellationToken cancellationToken
    )
    {
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        // REPEATABLE READ + FOR UPDATE SKIP LOCKED ensures concurrent workers see a consistent
        // snapshot and skip rows already locked by another worker.
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // Phase 1: lock and collect IDs
            var ids = new List<byte[]>();

            await using (var selectCmd = new MySqlCommand(selectIdsSql, connection, transaction))
            {
                _ = selectCmd.Parameters.AddWithValue("@batchSize", batchSize);
                _ = selectCmd.Parameters.AddWithValue("@nowTicks", nowTicks);

                if (maxRetryCount.HasValue)
                {
                    _ = selectCmd.Parameters.AddWithValue("@maxRetryCount", maxRetryCount.Value);
                }

                await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    ids.Add((byte[])reader[0]);
                }
            }

            if (ids.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return [];
            }

            var inParams = BuildInParameters(ids.Count);

            // Phase 2: claim the locked rows
            var updateSql = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                _updateToProcessingSqlTemplate,
                inParams
            );

            await using (var updateCmd = new MySqlCommand(updateSql, connection, transaction))
            {
                _ = updateCmd.Parameters.AddWithValue("@nowTicks", nowTicks);
                AddIdParameters(updateCmd, ids);

                _ = await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Phase 3: read the updated rows
            var selectSql = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                _selectByIdsSqlTemplate,
                inParams
            );

            IReadOnlyList<OutboxMessage> messages;

            await using (var fetchCmd = new MySqlCommand(selectSql, connection, transaction))
            {
                AddIdParameters(fetchCmd, ids);
                messages = await ReadMessagesAsync(fetchCmd, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return messages;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Opens and returns a new <see cref="MySqlConnection"/> using the stored connection string.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An open <see cref="MySqlConnection"/>.</returns>
    private async Task<MySqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Retrieves the current ambient <see cref="MySqlTransaction"/> from the registered
    /// <see cref="IOutboxTransactionScope"/>, or <see langword="null"/> if no scope is configured
    /// or no transaction is active.
    /// </summary>
    private MySqlTransaction? GetCurrentTransaction() => _transactionScope?.GetCurrentTransaction() as MySqlTransaction;

    /// <summary>
    /// Adds all <see cref="OutboxMessage"/> property values as typed parameters to a <see cref="MySqlCommand"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid"/> values are stored as <c>BINARY(16)</c> via <see cref="Guid.ToByteArray()"/>.
    /// <see cref="DateTimeOffset"/> values are stored as <c>BIGINT</c> UTC ticks.
    /// </remarks>
    /// <param name="command">The command to which parameters are added.</param>
    /// <param name="message">The outbox message providing parameter values.</param>
    private static void AddMessageParameters(MySqlCommand command, OutboxMessage message)
    {
        _ = command.Parameters.AddWithValue("@Id", message.Id.ToByteArray());
        _ = command.Parameters.AddWithValue("@EventType", message.EventType.ToOutboxEventTypeName());
        _ = command.Parameters.AddWithValue("@Payload", message.Payload);
        _ = command.Parameters.AddWithValue("@CorrelationId", (object?)message.CorrelationId ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@CausationId", (object?)message.CausationId ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@CreatedAt", message.CreatedAt.UtcTicks);
        _ = command.Parameters.AddWithValue("@UpdatedAt", message.UpdatedAt.UtcTicks);
        _ = command.Parameters.AddWithValue(
            "@ProcessedAt",
            message.ProcessedAt.HasValue ? (object)message.ProcessedAt.Value.UtcTicks : DBNull.Value
        );
        _ = command.Parameters.AddWithValue(
            "@NextRetryAt",
            message.NextRetryAt.HasValue ? (object)message.NextRetryAt.Value.UtcTicks : DBNull.Value
        );
        _ = command.Parameters.AddWithValue("@RetryCount", message.RetryCount);
        _ = command.Parameters.AddWithValue("@Error", (object?)message.Error ?? DBNull.Value);
        _ = command.Parameters.AddWithValue("@Status", (int)message.Status);
    }

    /// <summary>
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="OutboxMessage"/> instances
    /// using pre-resolved column ordinals for maximum performance.
    /// </summary>
    /// <param name="command">The <see cref="MySqlCommand"/> to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of <see cref="OutboxMessage"/> records.</returns>
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

    /// <summary>
    /// Builds a comma-separated list of positional parameter names, e.g. <c>@id0, @id1, @id2</c>,
    /// for use in <c>IN</c> clauses when the count is only known at runtime.
    /// </summary>
    /// <param name="count">The number of parameter placeholders to generate.</param>
    /// <returns>A string suitable for embedding in an <c>IN (…)</c> clause.</returns>
    private static string BuildInParameters(int count)
    {
        var sb = new StringBuilder(count * 5);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                _ = sb.Append(", ");
            }

            _ = sb.Append("@id").Append(i);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Adds a parameter <c>@id{i}</c> for each element of <paramref name="ids"/>.
    /// Each value is the 16-byte binary representation of a <see cref="Guid"/> matching the
    /// <c>BINARY(16)</c> column type.
    /// </summary>
    private static void AddIdParameters(MySqlCommand command, List<byte[]> ids)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            _ = command.Parameters.AddWithValue($"@id{i}", ids[i]);
        }
    }
}
