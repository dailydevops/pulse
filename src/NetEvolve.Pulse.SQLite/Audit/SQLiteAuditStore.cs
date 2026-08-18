namespace NetEvolve.Pulse.Audit;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQLite implementation of <see cref="IAuditStore"/> using ADO.NET.
/// Provides audit trail persistence optimized for SQLite.
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
internal sealed class SQLiteAuditStore : IAuditStore
{
    /// <summary>The SQLite connection string used to open new connections for each store operation.</summary>
    private readonly string _connectionString;

    /// <summary>Whether to apply WAL journal mode to the database.</summary>
    private readonly bool _enableWalMode;

    /// <summary>Tracks whether the persistent WAL journal mode has already been applied to the database.</summary>
    private int _walModeApplied;

    /// <summary>Cached SQL statement for inserting an audit record.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteAuditStore"/> class.
    /// </summary>
    /// <param name="options">The audit trail configuration options.</param>
    public SQLiteAuditStore(IOptions<AuditStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;
        _enableWalMode = opts.EnableWalMode;

        SqlIdentifier.Validate(opts.TableName, nameof(opts.TableName));
        var table = $"\"{opts.TableName}\"";

        _insertSql = $"""
            INSERT INTO {table}
            ("{AuditEntrySchema.Columns.Id}",
             "{AuditEntrySchema.Columns.CommandType}",
             "{AuditEntrySchema.Columns.UserId}",
             "{AuditEntrySchema.Columns.CorrelationId}",
             "{AuditEntrySchema.Columns.OccurredAt}",
             "{AuditEntrySchema.Columns.DurationMs}",
             "{AuditEntrySchema.Columns.Result}",
             "{AuditEntrySchema.Columns.Payload}",
             "{AuditEntrySchema.Columns.ExceptionMessage}")
            VALUES (@id, @commandType, @userId, @correlationId, @occurredAt, @durationMs, @result, @payload, @exceptionMessage);
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
            var command = new SqliteCommand(_insertSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@id", record.Id.ToString());
                _ = command.Parameters.AddWithValue("@commandType", record.CommandType);
                _ = command.Parameters.AddWithValue("@userId", (object?)record.UserId ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("@correlationId", (object?)record.CorrelationId ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("@occurredAt", record.OccurredAt);
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
    /// Opens and returns a new <see cref="SqliteConnection"/> using the stored connection string.
    /// Applies WAL mode once per store instance when <see cref="AuditStoreOptions.EnableWalMode"/> is
    /// <see langword="true"/>; the journal mode is a persistent database property and does not need
    /// to be re-applied on subsequent connections.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An open <see cref="SqliteConnection"/>.</returns>
    private async Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
}
