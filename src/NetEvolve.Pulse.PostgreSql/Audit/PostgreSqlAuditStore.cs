namespace NetEvolve.Pulse.Audit;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.Outbox;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of <see cref="IAuditStore"/> using ADO.NET.
/// Provides audit trail persistence optimized for PostgreSQL.
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
internal sealed class PostgreSqlAuditStore : IAuditStore
{
    /// <summary>The PostgreSQL connection string used to open new connections for each operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached SQL for inserting an audit record.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlAuditStore"/> class.
    /// </summary>
    /// <param name="options">The audit trail store configuration options.</param>
    public PostgreSqlAuditStore(IOptions<AuditStoreOptions> options)
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

        var qualifiedTableName = $"\"{schema}\".\"{tableName}\"";

        _insertSql = $"""
            INSERT INTO {qualifiedTableName}
                ("{AuditEntrySchema.Columns.Id}", "{AuditEntrySchema.Columns.CommandType}", "{AuditEntrySchema.Columns.UserId}", "{AuditEntrySchema.Columns.CorrelationId}", "{AuditEntrySchema.Columns.OccurredAt}", "{AuditEntrySchema.Columns.DurationMs}", "{AuditEntrySchema.Columns.Result}", "{AuditEntrySchema.Columns.Payload}", "{AuditEntrySchema.Columns.ExceptionMessage}")
            VALUES
                (@id, @command_type, @user_id, @correlation_id, @occurred_at, @duration_ms, @result, @payload, @exception_message)
            """;
    }

    /// <inheritdoc />
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(record);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(_insertSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("id", record.Id);
                _ = command.Parameters.AddWithValue("command_type", record.CommandType);
                _ = command.Parameters.AddWithValue("user_id", (object?)record.UserId ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("correlation_id", (object?)record.CorrelationId ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("occurred_at", record.OccurredAt);
                _ = command.Parameters.AddWithValue("duration_ms", record.DurationMs);
                _ = command.Parameters.AddWithValue("result", (short)record.Result);
                _ = command.Parameters.AddWithValue("payload", (object?)record.Payload ?? DBNull.Value);
                _ = command.Parameters.AddWithValue(
                    "exception_message",
                    (object?)record.ExceptionMessage ?? DBNull.Value
                );

                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
