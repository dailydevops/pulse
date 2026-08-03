namespace NetEvolve.Pulse.Audit;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQL Server implementation of <see cref="IAuditManagement"/> using ADO.NET.
/// Provides filtered querying and statistics for the audit trail store.
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
    Justification = "The SQL command text is constructed from validated AuditStoreOptions.Schema/TableName properties and static filter clauses parameterized via SqlParameter, not user input."
)]
internal sealed class SqlServerAuditManagement : IAuditManagement
{
    /// <summary>The SQL Server connection string used to open new connections for each management operation.</summary>
    private readonly string _connectionString;

    /// <summary>The fully qualified, bracket-quoted table name used to build query SQL.</summary>
    private readonly string _fullTableName;

    /// <summary>Cached SQL command text for retrieving aggregate result counts.</summary>
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerAuditManagement"/> class.
    /// </summary>
    /// <param name="options">The audit store configuration options.</param>
    public SqlServerAuditManagement(IOptions<AuditStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        _connectionString = options.Value.ConnectionString;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? AuditEntrySchema.DefaultSchema
            : options.Value.Schema;
        SqlIdentifier.Validate(schema, nameof(options.Value.Schema));
        SqlIdentifier.Validate(options.Value.TableName, nameof(options.Value.TableName));

        _fullTableName = $"[{schema}].[{options.Value.TableName}]";

        _getStatisticsSql = $"""
            SELECT [{AuditEntrySchema.Columns.Result}], COUNT(*) AS [Count]
            FROM {_fullTableName}
            GROUP BY [{AuditEntrySchema.Columns.Result}]
            """;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditRecord>> QueryAsync(
        AuditFilter filter,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(filter);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var sql = BuildQuerySql(filter, out var parameters);

            var command = new SqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                foreach (var (name, value) in parameters)
                {
                    _ = command.Parameters.AddWithValue(name, value);
                }

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
            var command = new SqlCommand(_getStatisticsSql, connection);
            await using (command.ConfigureAwait(false))
            {
                var successCount = 0;
                var failureCount = 0;

                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    var ordResult = reader.GetOrdinal(AuditEntrySchema.Columns.Result);
                    var ordCount = reader.GetOrdinal("Count");

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var result = (AuditResult)reader.GetInt16(ordResult);
                        var count = reader.GetInt32(ordCount);

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

                return new AuditStatistics(successCount, failureCount);
            }
        }
    }

    /// <summary>
    /// Builds the parameterized SQL SELECT statement for the given <paramref name="filter"/>.
    /// </summary>
    /// <param name="filter">The filter conditions to translate into a dynamic WHERE clause.</param>
    /// <param name="parameters">The parameters to apply to the returned SQL text.</param>
    /// <returns>The parameterized SQL SELECT statement text.</returns>
    private string BuildQuerySql(AuditFilter filter, out List<(string Name, object Value)> parameters)
    {
        parameters = [];

        var sql = new StringBuilder();
        _ = sql.Append("SELECT [")
            .Append(AuditEntrySchema.Columns.Id)
            .Append("], [")
            .Append(AuditEntrySchema.Columns.CommandType)
            .Append("], [")
            .Append(AuditEntrySchema.Columns.UserId)
            .Append("], [")
            .Append(AuditEntrySchema.Columns.CorrelationId)
            .Append("], [")
            .Append(AuditEntrySchema.Columns.OccurredAt)
            .Append("], [")
            .Append(AuditEntrySchema.Columns.DurationMs)
            .Append("], [")
            .Append(AuditEntrySchema.Columns.Result)
            .Append("], [")
            .Append(AuditEntrySchema.Columns.Payload)
            .Append("], [")
            .Append(AuditEntrySchema.Columns.ExceptionMessage)
            .Append("]\nFROM ")
            .Append(_fullTableName);

        var conditions = new List<string>();

        if (filter.CommandType is not null)
        {
            conditions.Add($"[{AuditEntrySchema.Columns.CommandType}] = @CommandType");
            parameters.Add(("@CommandType", filter.CommandType));
        }

        if (filter.UserId is not null)
        {
            conditions.Add($"[{AuditEntrySchema.Columns.UserId}] = @UserId");
            parameters.Add(("@UserId", filter.UserId));
        }

        if (filter.From is not null)
        {
            conditions.Add($"[{AuditEntrySchema.Columns.OccurredAt}] >= @From");
            parameters.Add(("@From", filter.From.Value));
        }

        if (filter.To is not null)
        {
            conditions.Add($"[{AuditEntrySchema.Columns.OccurredAt}] <= @To");
            parameters.Add(("@To", filter.To.Value));
        }

        if (filter.Result is not null)
        {
            conditions.Add($"[{AuditEntrySchema.Columns.Result}] = @Result");
            parameters.Add(("@Result", (short)filter.Result.Value));
        }

        if (conditions.Count > 0)
        {
            _ = sql.Append("\nWHERE ").Append(string.Join(" AND ", conditions));
        }

        _ = sql.Append("\nORDER BY [")
            .Append(AuditEntrySchema.Columns.OccurredAt)
            .Append("] DESC")
            .Append("\nOFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY");

        parameters.Add(("@Skip", filter.Skip));
        parameters.Add(("@Take", filter.Take));

        return sql.ToString();
    }

    /// <summary>
    /// Creates and opens a new SQL Server connection.
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
    /// Maps the current row of a <see cref="SqlDataReader"/> to a new <see cref="AuditRecord"/> instance.
    /// </summary>
    /// <param name="reader">The reader positioned on the row to map.</param>
    /// <returns>A populated <see cref="AuditRecord"/>.</returns>
    private static AuditRecord MapToRecord(SqlDataReader reader)
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
            OccurredAt = reader.GetDateTimeOffset(ordOccurredAt),
            DurationMs = reader.GetDouble(ordDurationMs),
            Result = (AuditResult)reader.GetInt16(ordResult),
            Payload = reader.IsDBNull(ordPayload) ? null : reader.GetString(ordPayload),
            ExceptionMessage = reader.IsDBNull(ordExceptionMessage) ? null : reader.GetString(ordExceptionMessage),
        };
    }
}
