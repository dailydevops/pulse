namespace NetEvolve.Pulse.Idempotency;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// MySQL implementation of <see cref="IIdempotencyKeyRepository"/> using ADO.NET.
/// Provides idempotency key persistence optimized for MySQL.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/IdempotencyKey.sql</c> to create the required
/// database objects before using this provider.
/// <para><strong>Schema:</strong></para>
/// MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
/// All tables reside in the active database specified by the connection string.
/// The <see cref="IdempotencyKeyOptions.Schema"/> property is ignored for MySQL.
/// <para><strong>Duplicate Key Handling:</strong></para>
/// Uses <c>INSERT IGNORE</c> to handle duplicate key inserts gracefully.
/// Concurrent inserts of the same key are idempotent and will not throw exceptions.
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
    Justification = "SQL is constructed from validated IdempotencyKeyOptions.TableName property, not user input."
)]
internal sealed class MySqlIdempotencyKeyRepository : IIdempotencyKeyRepository
{
    /// <summary>The MySQL connection string used to open new connections for each repository operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached SQL statement for checking if an idempotency key exists (no TTL).</summary>
    private readonly string _existsSql;

    /// <summary>Cached SQL statement for checking if an idempotency key exists within its TTL window.</summary>
    private readonly string _existsWithTtlSql;

    /// <summary>Cached SQL statement for inserting an idempotency key.</summary>
    private readonly string _insertSql;

    /// <summary>Cached SQL statement for refreshing the timestamp of an expired idempotency key.</summary>
    private readonly string _refreshExpiredSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlIdempotencyKeyRepository"/> class.
    /// </summary>
    /// <param name="options">The idempotency key configuration options.</param>
    public MySqlIdempotencyKeyRepository(IOptions<IdempotencyKeyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;

        var table = opts.FullTableName;

        _existsSql = $"""
            SELECT 1 FROM {table}
            WHERE `{IdempotencyKeySchema.Columns.IdempotencyKey}` = @key
            LIMIT 1
            """;

        _existsWithTtlSql = $"""
            SELECT 1 FROM {table}
            WHERE `{IdempotencyKeySchema.Columns.IdempotencyKey}` = @key
              AND `{IdempotencyKeySchema.Columns.CreatedAt}` >= @validFromTicks
            LIMIT 1
            """;

        // INSERT IGNORE silently discards the new row when the primary key already exists,
        // making concurrent inserts of the same key idempotent.
        _insertSql = $"""
            INSERT IGNORE INTO {table}
                (`{IdempotencyKeySchema.Columns.IdempotencyKey}`, `{IdempotencyKeySchema.Columns.CreatedAt}`)
            VALUES (@key, @createdAtTicks)
            """;

        // Only an expired row matches, so the affected-row count is unambiguous even with the
        // driver's default found-rows semantics (unlike INSERT ... ON DUPLICATE KEY UPDATE, which
        // reports 1 for both an insert and an unchanged duplicate).
        _refreshExpiredSql = $"""
            UPDATE {table}
            SET `{IdempotencyKeySchema.Columns.CreatedAt}` = @createdAtTicks
            WHERE `{IdempotencyKeySchema.Columns.IdempotencyKey}` = @key
              AND `{IdempotencyKeySchema.Columns.CreatedAt}` < @validFromTicks
            """;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string idempotencyKey,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var sql = validFrom.HasValue ? _existsWithTtlSql : _existsSql;

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new MySqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@key", idempotencyKey);

                if (validFrom.HasValue)
                {
                    _ = command.Parameters.AddWithValue("@validFromTicks", validFrom.Value.UtcTicks);
                }

                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result is not null and not DBNull;
            }
        }
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new MySqlCommand(_insertSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@key", idempotencyKey);
                _ = command.Parameters.AddWithValue("@createdAtTicks", createdAt.UtcTicks);

                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs <c>INSERT IGNORE</c> and, when <paramref name="validFrom"/> is set, a conditional
    /// <c>UPDATE</c> of an expired row. InnoDB's row lock makes a concurrent refresh re-read the
    /// already refreshed row, so only one caller for the same key receives <see langword="true"/>.
    /// When the refresh matches no row, the <c>INSERT IGNORE</c> runs once more, because the expired row
    /// may have been deleted by a cleanup job between the two statements.
    /// </remarks>
    public async Task<bool> TryReserveAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var insert = new MySqlCommand(_insertSql, connection);
            await using (insert.ConfigureAwait(false))
            {
                _ = insert.Parameters.AddWithValue("@key", idempotencyKey);
                _ = insert.Parameters.AddWithValue("@createdAtTicks", createdAt.UtcTicks);

                if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0)
                {
                    return true;
                }
            }

            if (!validFrom.HasValue)
            {
                return false;
            }

            var refresh = new MySqlCommand(_refreshExpiredSql, connection);
            await using (refresh.ConfigureAwait(false))
            {
                _ = refresh.Parameters.AddWithValue("@key", idempotencyKey);
                _ = refresh.Parameters.AddWithValue("@createdAtTicks", createdAt.UtcTicks);
                _ = refresh.Parameters.AddWithValue("@validFromTicks", validFrom.Value.UtcTicks);

                if (await refresh.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0)
                {
                    return true;
                }
            }

            // A cleanup job can delete the expired row between the two statements; the key is then
            // absent, so one more INSERT IGNORE reserves it instead of reporting a false duplicate.
            var retry = new MySqlCommand(_insertSql, connection);
            await using (retry.ConfigureAwait(false))
            {
                _ = retry.Parameters.AddWithValue("@key", idempotencyKey);
                _ = retry.Parameters.AddWithValue("@createdAtTicks", createdAt.UtcTicks);

                return await retry.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
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
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
