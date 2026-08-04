namespace NetEvolve.Pulse.Audit;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.Outbox;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of <see cref="IAuditManagement"/> using ADO.NET.
/// Provides audit trail querying and statistics for PostgreSQL.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/AuditEntry.sql</c> to create the required
/// database objects before using this provider.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "The table name is constructed from validated AuditStoreOptions.Schema/TableName properties, not user input."
)]
internal sealed class PostgreSqlAuditManagement : IAuditManagement
{
    /// <summary>The PostgreSQL connection string used to open new connections for each operation.</summary>
    private readonly string _connectionString;

    /// <summary>The fully qualified table name (schema and table, quoted).</summary>
    private readonly string _qualifiedTableName;

    /// <summary>The comma-separated, quoted column list used by SELECT queries.</summary>
    private readonly string _columns;

    /// <summary>Cached SQL for aggregating record counts per result.</summary>
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlAuditManagement"/> class.
    /// </summary>
    /// <param name="options">The audit trail store configuration options.</param>
    public PostgreSqlAuditManagement(IOptions<AuditStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        _connectionString = options.Value.ConnectionString;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? AuditEntrySchema.DefaultSchema
            : options.Value.Schema;
        SqlIdentifier.Validate(schema, nameof(options.Value.Schema));

        var tableName = string.IsNullOrWhiteSpace(options.Value.TableName)
            ? AuditEntrySchema.DefaultTableName
            : options.Value.TableName;
        SqlIdentifier.Validate(tableName, nameof(options.Value.TableName));

        _qualifiedTableName = $"\"{schema}\".\"{tableName}\"";

        _columns =
            $"\"{AuditEntrySchema.Columns.Id}\", \"{AuditEntrySchema.Columns.CommandType}\", "
            + $"\"{AuditEntrySchema.Columns.UserId}\", \"{AuditEntrySchema.Columns.CorrelationId}\", "
            + $"\"{AuditEntrySchema.Columns.OccurredAt}\", \"{AuditEntrySchema.Columns.DurationMs}\", "
            + $"\"{AuditEntrySchema.Columns.Result}\", \"{AuditEntrySchema.Columns.Payload}\", "
            + $"\"{AuditEntrySchema.Columns.ExceptionMessage}\"";

        _getStatisticsSql = $"""
            SELECT "{AuditEntrySchema.Columns.Result}", COUNT(*)
            FROM {_qualifiedTableName}
            GROUP BY "{AuditEntrySchema.Columns.Result}"
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
        var conditions = new List<string>();

        if (filter.CommandType is not null)
        {
            conditions.Add($"\"{AuditEntrySchema.Columns.CommandType}\" = @command_type");
        }

        if (filter.UserId is not null)
        {
            conditions.Add($"\"{AuditEntrySchema.Columns.UserId}\" = @user_id");
        }

        if (filter.From is not null)
        {
            conditions.Add($"\"{AuditEntrySchema.Columns.OccurredAt}\" >= @from");
        }

        if (filter.To is not null)
        {
            conditions.Add($"\"{AuditEntrySchema.Columns.OccurredAt}\" <= @to");
        }

        if (filter.Result is not null)
        {
            conditions.Add($"\"{AuditEntrySchema.Columns.Result}\" = @result");
        }

        if (conditions.Count > 0)
        {
            _ = whereClause.Append("WHERE ").Append(string.Join(" AND ", conditions));
        }

        var querySql = $"""
            SELECT {_columns}
            FROM {_qualifiedTableName}
            {whereClause}
            ORDER BY "{AuditEntrySchema.Columns.OccurredAt}" DESC
            LIMIT @take
            OFFSET @skip
            """;

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(querySql, connection);
            await using (command.ConfigureAwait(false))
            {
                if (filter.CommandType is not null)
                {
                    _ = command.Parameters.AddWithValue("command_type", filter.CommandType);
                }

                if (filter.UserId is not null)
                {
                    _ = command.Parameters.AddWithValue("user_id", filter.UserId);
                }

                if (filter.From is not null)
                {
                    _ = command.Parameters.AddWithValue("from", filter.From.Value);
                }

                if (filter.To is not null)
                {
                    _ = command.Parameters.AddWithValue("to", filter.To.Value);
                }

                if (filter.Result is not null)
                {
                    _ = command.Parameters.AddWithValue("result", (short)filter.Result.Value);
                }

                _ = command.Parameters.AddWithValue("take", filter.Take);
                _ = command.Parameters.AddWithValue("skip", filter.Skip);

                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    var records = new List<AuditRecord>();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        records.Add(MapToRecord(reader));
                    }

                    return records;
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<AuditStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(_getStatisticsSql, connection);
            await using (command.ConfigureAwait(false))
            {
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    var successCount = 0;
                    var failureCount = 0;

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var result = (AuditResult)reader.GetInt16(0);
                        var groupCount = Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture);

                        switch (result)
                        {
                            case AuditResult.Success:
                                successCount = groupCount;
                                break;
                            case AuditResult.Failure:
                                failureCount = groupCount;
                                break;
                        }
                    }

                    return new AuditStatistics(successCount, failureCount);
                }
            }
        }
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
    /// Maps the current row of a <see cref="NpgsqlDataReader"/> to a new <see cref="AuditRecord"/> instance.
    /// </summary>
    private static AuditRecord MapToRecord(NpgsqlDataReader reader)
    {
        var ordId = reader.GetOrdinal(AuditEntrySchema.Columns.Id);
        var ordCommandType = reader.GetOrdinal(AuditEntrySchema.Columns.CommandType);
        var ordUserId = reader.GetOrdinal(AuditEntrySchema.Columns.UserId);
        var ordCorrelationId = reader.GetOrdinal(AuditEntrySchema.Columns.CorrelationId);
        var ordOccurredAt = reader.GetOrdinal(AuditEntrySchema.Columns.OccurredAt);
        var ordDurationMs = reader.GetOrdinal(AuditEntrySchema.Columns.DurationMs);
        var ordResult = reader.GetOrdinal(AuditEntrySchema.Columns.Result);
        var ordPayload = reader.GetOrdinal(AuditEntrySchema.Columns.Payload);
        var ordExceptionMessage = reader.GetOrdinal(AuditEntrySchema.Columns.ExceptionMessage);

        return new AuditRecord
        {
            Id = reader.GetGuid(ordId),
            CommandType = reader.GetString(ordCommandType),
            UserId = reader.IsDBNull(ordUserId) ? null : reader.GetString(ordUserId),
            CorrelationId = reader.IsDBNull(ordCorrelationId) ? null : reader.GetString(ordCorrelationId),
            OccurredAt = reader.GetFieldValue<DateTimeOffset>(ordOccurredAt),
            DurationMs = reader.GetDouble(ordDurationMs),
            Result = (AuditResult)reader.GetInt16(ordResult),
            Payload = reader.IsDBNull(ordPayload) ? null : reader.GetString(ordPayload),
            ExceptionMessage = reader.IsDBNull(ordExceptionMessage) ? null : reader.GetString(ordExceptionMessage),
        };
    }
}
