namespace NetEvolve.Pulse.Audit;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// MySQL implementation of <see cref="IAuditStore"/> using ADO.NET.
/// Provides audit trail persistence optimized for MySQL.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/AuditEntry.sql</c> to create the required
/// database objects before using this provider.
/// <para><strong>Schema:</strong></para>
/// MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
/// All tables reside in the active database specified by the connection string.
/// The <see cref="AuditStoreOptions.Schema"/> property is ignored for MySQL.
/// <para><strong>Timestamps:</strong></para>
/// Stores <see cref="DateTimeOffset"/> values as <c>BIGINT</c> (UTC ticks), matching the
/// interoperability contract with the Entity Framework MySQL provider.
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
internal sealed class MySqlAuditStore : IAuditStore
{
    /// <summary>The MySQL connection string used to open new connections for each store operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached SQL statement for inserting an audit record.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlAuditStore"/> class.
    /// </summary>
    /// <param name="options">The audit trail store configuration options.</param>
    public MySqlAuditStore(IOptions<AuditStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;

        SqlIdentifier.Validate(opts.TableName, nameof(opts.TableName));
        var table = $"`{opts.TableName}`";

        _insertSql = $"""
            INSERT INTO {table}
                (`{AuditEntrySchema.Columns.Id}`,
                 `{AuditEntrySchema.Columns.CommandType}`,
                 `{AuditEntrySchema.Columns.UserId}`,
                 `{AuditEntrySchema.Columns.CorrelationId}`,
                 `{AuditEntrySchema.Columns.OccurredAt}`,
                 `{AuditEntrySchema.Columns.DurationMs}`,
                 `{AuditEntrySchema.Columns.Result}`,
                 `{AuditEntrySchema.Columns.Payload}`,
                 `{AuditEntrySchema.Columns.ExceptionMessage}`)
            VALUES
                (@id, @commandType, @userId, @correlationId, @occurredAtTicks, @durationMs, @result, @payload, @exceptionMessage)
            """;
    }

    /// <inheritdoc />
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new MySqlCommand(_insertSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@id", record.Id.ToByteArray());
                _ = command.Parameters.AddWithValue("@commandType", record.CommandType);
                _ = command.Parameters.AddWithValue("@userId", (object?)record.UserId ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("@correlationId", (object?)record.CorrelationId ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("@occurredAtTicks", record.OccurredAt.UtcTicks);
                _ = command.Parameters.AddWithValue("@durationMs", record.DurationMs);
                _ = command.Parameters.AddWithValue("@result", (int)record.Result);
                _ = command.Parameters.AddWithValue("@payload", (object?)record.Payload ?? DBNull.Value);
                _ = command.Parameters.AddWithValue(
                    "@exceptionMessage",
                    (object?)record.ExceptionMessage ?? DBNull.Value
                );

                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
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
}
