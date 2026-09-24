namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQLite implementation of <see cref="ICommandDeadLetterStore"/> using ADO.NET.
/// Provides command dead letter persistence optimized for SQLite.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/003_CreateCommandDeadLetterTable.sql</c> to create the
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
    Justification = "SQL is constructed from validated CommandDeadLetterOptions.TableName property, not user input."
)]
internal sealed class SQLiteCommandDeadLetterStore : ICommandDeadLetterStore
{
    /// <summary>The SQLite connection string used to open new connections for each store operation.</summary>
    private readonly string _connectionString;

    /// <summary>Whether to apply WAL journal mode to the database.</summary>
    private readonly bool _enableWalMode;

    /// <summary>Tracks whether the persistent WAL journal mode has already been applied to the database.</summary>
    private int _walModeApplied;

    /// <summary>Cached SQL statement for inserting a command dead letter entry.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteCommandDeadLetterStore"/> class.
    /// </summary>
    /// <param name="options">The command dead letter configuration options.</param>
    public SQLiteCommandDeadLetterStore(IOptions<CommandDeadLetterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;
        _enableWalMode = opts.EnableWalMode;

        SqlIdentifier.Validate(opts.TableName, nameof(opts.TableName));
        var table = $"\"{opts.TableName}\"";

        _insertSql = $"""
            INSERT OR IGNORE INTO {table}
            ("{CommandDeadLetterSchema.Columns.Id}",
             "{CommandDeadLetterSchema.Columns.CommandType}",
             "{CommandDeadLetterSchema.Columns.Payload}",
             "{CommandDeadLetterSchema.Columns.ExceptionType}",
             "{CommandDeadLetterSchema.Columns.ExceptionMessage}",
             "{CommandDeadLetterSchema.Columns.OccurredAt}",
             "{CommandDeadLetterSchema.Columns.AttemptCount}",
             "{CommandDeadLetterSchema.Columns.Status}")
            VALUES (@id, @commandType, @payload, @exceptionType, @exceptionMessage, @occurredAt, @attemptCount, @status);
            """;
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        string commandType,
        string payload,
        Exception exception,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentNullException.ThrowIfNull(exception);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new SqliteCommand(_insertSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                _ = command.Parameters.AddWithValue("@commandType", commandType);
                _ = command.Parameters.AddWithValue("@payload", payload);
                _ = command.Parameters.AddWithValue("@exceptionType", exception.GetType().AssemblyQualifiedName);
                _ = command.Parameters.AddWithValue("@exceptionMessage", exception.Message);
                _ = command.Parameters.AddWithValue("@occurredAt", DateTimeOffset.UtcNow);
                _ = command.Parameters.AddWithValue("@attemptCount", 1);
                _ = command.Parameters.AddWithValue("@status", (int)CommandDeadLetterStatus.New);

                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Opens and returns a new <see cref="SqliteConnection"/> using the stored connection string.
    /// Applies WAL mode once per store instance when <see cref="CommandDeadLetterOptions.EnableWalMode"/> is
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
