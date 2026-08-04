namespace NetEvolve.Pulse.Audit;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQLite implementation of <see cref="IAuditManagement"/> using ADO.NET.
/// Provides audit trail querying and statistics.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/004_CreateAuditEntryTable.sql</c> to create the
/// required database objects before using this provider.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is constructed from validated AuditStoreOptions.TableName property, not user input."
)]
internal sealed class SQLiteAuditManagement : IAuditManagement
{
    /// <summary>The SQLite connection string resolved from <see cref="AuditStoreOptions"/>.</summary>
    private readonly string _connectionString;

    /// <summary>Whether to apply WAL journal mode to the database.</summary>
    private readonly bool _enableWalMode;

    /// <summary>Tracks whether the persistent WAL journal mode has already been applied to the database.</summary>
    private int _walModeApplied;

    /// <summary>The qualified table name used to build query statements.</summary>
    private readonly string _table;

    /// <summary>Cached SQL statement for retrieving audit statistics.</summary>
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteAuditManagement"/> class.
    /// </summary>
    /// <param name="options">The audit trail configuration options.</param>
    public SQLiteAuditManagement(IOptions<AuditStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;
        _enableWalMode = opts.EnableWalMode;

        SqlIdentifier.Validate(opts.TableName, nameof(opts.TableName));
        _table = $"\"{opts.TableName}\"";

        _getStatisticsSql = $"""
            SELECT "{AuditEntrySchema.Columns.Result}", COUNT(*)
            FROM {_table}
            GROUP BY "{AuditEntrySchema.Columns.Result}";
            """;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditRecord>> QueryAsync(
        AuditFilter filter,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(filter);

        var whereClause = new StringBuilder();

        if (filter.CommandType is not null)
        {
            var keyword = whereClause.Length == 0 ? "WHERE " : "AND ";
            _ = whereClause
                .Append(keyword)
                .Append('"')
                .Append(AuditEntrySchema.Columns.CommandType)
                .Append("\" = @commandType ");
        }

        if (filter.UserId is not null)
        {
            var keyword = whereClause.Length == 0 ? "WHERE " : "AND ";
            _ = whereClause.Append(keyword).Append('"').Append(AuditEntrySchema.Columns.UserId).Append("\" = @userId ");
        }

        if (filter.From is not null)
        {
            var keyword = whereClause.Length == 0 ? "WHERE " : "AND ";
            _ = whereClause
                .Append(keyword)
                .Append('"')
                .Append(AuditEntrySchema.Columns.OccurredAt)
                .Append("\" >= @from ");
        }

        if (filter.To is not null)
        {
            var keyword = whereClause.Length == 0 ? "WHERE " : "AND ";
            _ = whereClause
                .Append(keyword)
                .Append('"')
                .Append(AuditEntrySchema.Columns.OccurredAt)
                .Append("\" <= @to ");
        }

        if (filter.Result is not null)
        {
            var keyword = whereClause.Length == 0 ? "WHERE " : "AND ";
            _ = whereClause.Append(keyword).Append('"').Append(AuditEntrySchema.Columns.Result).Append("\" = @result ");
        }

        var querySql = $"""
            SELECT
                "{AuditEntrySchema.Columns.Id}",
                "{AuditEntrySchema.Columns.CommandType}",
                "{AuditEntrySchema.Columns.UserId}",
                "{AuditEntrySchema.Columns.CorrelationId}",
                "{AuditEntrySchema.Columns.OccurredAt}",
                "{AuditEntrySchema.Columns.DurationMs}",
                "{AuditEntrySchema.Columns.Result}",
                "{AuditEntrySchema.Columns.Payload}",
                "{AuditEntrySchema.Columns.ExceptionMessage}"
            FROM {_table}
            {whereClause}
            ORDER BY "{AuditEntrySchema.Columns.OccurredAt}" DESC
            LIMIT @take OFFSET @skip;
            """;

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
#pragma warning disable S2077 // querySql is built exclusively from the fixed column list, validated table name, and static WHERE keywords; all filter values are bound as parameters
            var command = new SqliteCommand(querySql, connection);
#pragma warning restore S2077
            await using (command.ConfigureAwait(false))
            {
                if (filter.CommandType is not null)
                {
                    _ = command.Parameters.AddWithValue("@commandType", filter.CommandType);
                }

                if (filter.UserId is not null)
                {
                    _ = command.Parameters.AddWithValue("@userId", filter.UserId);
                }

                if (filter.From is not null)
                {
                    _ = command.Parameters.AddWithValue("@from", filter.From.Value);
                }

                if (filter.To is not null)
                {
                    _ = command.Parameters.AddWithValue("@to", filter.To.Value);
                }

                if (filter.Result is not null)
                {
                    _ = command.Parameters.AddWithValue("@result", (int)filter.Result.Value);
                }

                _ = command.Parameters.AddWithValue("@take", filter.Take);
                _ = command.Parameters.AddWithValue("@skip", filter.Skip);

                return await ReadRecordsAsync(command, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<AuditStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var successCount = 0;
        var failureCount = 0;

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new SqliteCommand(_getStatisticsSql, connection);
            await using (command.ConfigureAwait(false))
            {
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var result = (AuditResult)reader.GetInt64(0);
                        var count = (int)reader.GetInt64(1);

                        switch (result)
                        {
                            case AuditResult.Success:
                                successCount = count;
                                break;
                            case AuditResult.Failure:
                                failureCount = count;
                                break;
                        }
                    }
                }
            }
        }

        return new AuditStatistics(successCount, failureCount);
    }

    /// <summary>
    /// Opens and returns a new <see cref="SqliteConnection"/> using the stored connection string.
    /// Applies WAL mode once per instance when <see cref="AuditStoreOptions.EnableWalMode"/> is
    /// <see langword="true"/>; the journal mode is a persistent database property and does not need
    /// to be re-applied on subsequent connections.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An open <see cref="SqliteConnection"/>.</returns>
    private async Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (_enableWalMode && Volatile.Read(ref _walModeApplied) == 0)
        {
            var walCmd = new SqliteCommand("PRAGMA journal_mode=WAL;", connection);
            await using (walCmd.ConfigureAwait(false))
            {
                _ = await walCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            Volatile.Write(ref _walModeApplied, 1);
        }

        return connection;
    }

    /// <summary>
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="AuditRecord"/> instances.
    /// </summary>
    /// <param name="command">The <see cref="SqliteCommand"/> to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of <see cref="AuditRecord"/> instances.</returns>
    private static async Task<IReadOnlyList<AuditRecord>> ReadRecordsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken
    )
    {
        var records = new List<AuditRecord>();

        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return records;
            }

            var ordId = reader.GetOrdinal(AuditEntrySchema.Columns.Id);
            var ordCommandType = reader.GetOrdinal(AuditEntrySchema.Columns.CommandType);
            var ordUserId = reader.GetOrdinal(AuditEntrySchema.Columns.UserId);
            var ordCorrelationId = reader.GetOrdinal(AuditEntrySchema.Columns.CorrelationId);
            var ordOccurredAt = reader.GetOrdinal(AuditEntrySchema.Columns.OccurredAt);
            var ordDurationMs = reader.GetOrdinal(AuditEntrySchema.Columns.DurationMs);
            var ordResult = reader.GetOrdinal(AuditEntrySchema.Columns.Result);
            var ordPayload = reader.GetOrdinal(AuditEntrySchema.Columns.Payload);
            var ordExceptionMessage = reader.GetOrdinal(AuditEntrySchema.Columns.ExceptionMessage);

            do
            {
                var userIdNull = await reader.IsDBNullAsync(ordUserId, cancellationToken).ConfigureAwait(false);
                var correlationIdNull = await reader
                    .IsDBNullAsync(ordCorrelationId, cancellationToken)
                    .ConfigureAwait(false);
                var payloadNull = await reader.IsDBNullAsync(ordPayload, cancellationToken).ConfigureAwait(false);
                var exceptionMessageNull = await reader
                    .IsDBNullAsync(ordExceptionMessage, cancellationToken)
                    .ConfigureAwait(false);
                var occurredAt = await reader
                    .GetFieldValueAsync<DateTimeOffset>(ordOccurredAt, cancellationToken)
                    .ConfigureAwait(false);

                records.Add(
                    new AuditRecord
                    {
                        Id = Guid.Parse(reader.GetString(ordId)),
                        CommandType = reader.GetString(ordCommandType),
                        UserId = userIdNull ? null : reader.GetString(ordUserId),
                        CorrelationId = correlationIdNull ? null : reader.GetString(ordCorrelationId),
                        OccurredAt = occurredAt,
                        DurationMs = reader.GetDouble(ordDurationMs),
                        Result = (AuditResult)reader.GetInt64(ordResult),
                        Payload = payloadNull ? null : reader.GetString(ordPayload),
                        ExceptionMessage = exceptionMessageNull ? null : reader.GetString(ordExceptionMessage),
                    }
                );
            } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));

            return records;
        }
    }
}
