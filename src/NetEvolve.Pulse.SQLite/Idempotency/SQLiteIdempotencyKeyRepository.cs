namespace NetEvolve.Pulse.Idempotency;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// SQLite implementation of <see cref="IIdempotencyKeyRepository"/> using ADO.NET.
/// Provides idempotency key persistence optimized for SQLite.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/002_CreateIdempotencyKeyTable.sql</c> to create the required
/// database objects before using this provider.
/// <para><strong>Duplicate Key Handling:</strong></para>
/// Uses <c>INSERT OR IGNORE</c> to handle duplicate key inserts gracefully.
/// Concurrent inserts of the same key are idempotent and will not throw exceptions.
/// <para><strong>ISO-8601 Timestamps:</strong></para>
/// Stores <see cref="DateTimeOffset"/> values as ISO-8601 text strings, using SQLite's
/// native text affinity for reliable lexicographic ordering and TTL-based queries.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is constructed from validated IdempotencyKeyOptions.TableName property, not user input."
)]
internal sealed class SQLiteIdempotencyKeyRepository : IIdempotencyKeyRepository
{
    /// <summary>The SQLite connection string used to open new connections for each repository operation.</summary>
    private readonly string _connectionString;

    /// <summary>Whether to apply WAL journal mode on each opened connection.</summary>
    private readonly bool _enableWalMode;

    /// <summary>Cached SQL statement for checking if an idempotency key exists.</summary>
    private readonly string _existsSql;

    /// <summary>Cached SQL statement for checking if an idempotency key exists with TTL filtering.</summary>
    private readonly string _existsWithTtlSql;

    /// <summary>Cached SQL statement for inserting an idempotency key.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteIdempotencyKeyRepository"/> class.
    /// </summary>
    /// <param name="options">The idempotency key configuration options.</param>
    public SQLiteIdempotencyKeyRepository(IOptions<IdempotencyKeyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;
        _enableWalMode = opts.EnableWalMode;

        var table = opts.FullTableName;

        _existsSql = $"""
            SELECT 1 FROM {table}
            WHERE "{IdempotencyKeySchema.Columns.IdempotencyKey}" = @key
            LIMIT 1;
            """;

        _existsWithTtlSql = $"""
            SELECT 1 FROM {table}
            WHERE "{IdempotencyKeySchema.Columns.IdempotencyKey}" = @key
              AND "{IdempotencyKeySchema.Columns.CreatedAt}" >= @validFrom
            LIMIT 1;
            """;

        _insertSql = $"""
            INSERT OR IGNORE INTO {table}
            ("{IdempotencyKeySchema.Columns.IdempotencyKey}", "{IdempotencyKeySchema.Columns.CreatedAt}")
            VALUES (@key, @createdAt);
            """;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string idempotencyKey,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var sql = validFrom.HasValue ? _existsWithTtlSql : _existsSql;
            await using var command = new SqliteCommand(sql, connection);

            _ = command.Parameters.AddWithValue("@key", idempotencyKey);

            if (validFrom.HasValue)
            {
                _ = command.Parameters.AddWithValue("@validFrom", validFrom.Value.ToString("O"));
            }

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is not null and not DBNull;
        }
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = new SqliteCommand(_insertSql, connection);

            _ = command.Parameters.AddWithValue("@key", idempotencyKey);
            _ = command.Parameters.AddWithValue("@createdAt", createdAt.ToString("O"));

            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens and returns a new <see cref="SqliteConnection"/> using the stored connection string.
    /// Applies WAL mode when <see cref="IdempotencyKeyOptions.EnableWalMode"/> is <see langword="true"/>.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An open <see cref="SqliteConnection"/>.</returns>
    private async Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (_enableWalMode)
        {
            await using var walCmd = new SqliteCommand("PRAGMA journal_mode=WAL;", connection);
            _ = await walCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }
}
