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
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string idempotencyKey,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var sql = validFrom.HasValue ? _existsWithTtlSql : _existsSql;

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);

        _ = command.Parameters.AddWithValue("@key", idempotencyKey);

        if (validFrom.HasValue)
        {
            _ = command.Parameters.AddWithValue("@validFromTicks", validFrom.Value.UtcTicks);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null and not DBNull;
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(_insertSql, connection);

        _ = command.Parameters.AddWithValue("@key", idempotencyKey);
        _ = command.Parameters.AddWithValue("@createdAtTicks", createdAt.UtcTicks);

        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
