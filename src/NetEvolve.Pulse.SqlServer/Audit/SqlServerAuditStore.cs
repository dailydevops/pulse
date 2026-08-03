namespace NetEvolve.Pulse.Audit;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQL Server implementation of <see cref="IAuditStore"/> using ADO.NET.
/// Provides audit trail persistence optimized for SQL Server.
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
    Justification = "The SQL command text is constructed from validated AuditStoreOptions.Schema/TableName properties, not user input."
)]
internal sealed class SqlServerAuditStore : IAuditStore
{
    /// <summary>The SQL Server connection string used to open new connections for each store operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached SQL command text for inserting a new audit record.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerAuditStore"/> class.
    /// </summary>
    /// <param name="options">The audit store configuration options.</param>
    public SqlServerAuditStore(IOptions<AuditStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        _connectionString = options.Value.ConnectionString;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? AuditEntrySchema.DefaultSchema
            : options.Value.Schema;
        SqlIdentifier.Validate(schema, nameof(options.Value.Schema));
        SqlIdentifier.Validate(options.Value.TableName, nameof(options.Value.TableName));

        var fullTableName = $"[{schema}].[{options.Value.TableName}]";

        _insertSql = $"""
            INSERT INTO {fullTableName}
                ([{AuditEntrySchema.Columns.Id}],
                 [{AuditEntrySchema.Columns.CommandType}],
                 [{AuditEntrySchema.Columns.UserId}],
                 [{AuditEntrySchema.Columns.CorrelationId}],
                 [{AuditEntrySchema.Columns.OccurredAt}],
                 [{AuditEntrySchema.Columns.DurationMs}],
                 [{AuditEntrySchema.Columns.Result}],
                 [{AuditEntrySchema.Columns.Payload}],
                 [{AuditEntrySchema.Columns.ExceptionMessage}])
            VALUES
                (@Id, @CommandType, @UserId, @CorrelationId, @OccurredAt, @DurationMs, @Result, @Payload, @ExceptionMessage)
            """;
    }

    /// <inheritdoc />
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new SqlCommand(_insertSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@Id", record.Id);
                _ = command.Parameters.AddWithValue("@CommandType", record.CommandType);
                _ = command.Parameters.AddWithValue("@UserId", (object?)record.UserId ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("@CorrelationId", (object?)record.CorrelationId ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("@OccurredAt", record.OccurredAt);
                _ = command.Parameters.AddWithValue("@DurationMs", record.DurationMs);
                _ = command.Parameters.AddWithValue("@Result", (short)record.Result);
                _ = command.Parameters.AddWithValue("@Payload", (object?)record.Payload ?? DBNull.Value);
                _ = command.Parameters.AddWithValue(
                    "@ExceptionMessage",
                    (object?)record.ExceptionMessage ?? DBNull.Value
                );

                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
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
}
