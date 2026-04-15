namespace NetEvolve.Pulse.Idempotency;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Idempotency;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of <see cref="IIdempotencyKeyRepository"/> using ADO.NET.
/// Provides idempotency key persistence optimized for PostgreSQL.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/IdempotencyKey.sql</c> to create the required
/// database objects before using this provider.
/// <para><strong>Duplicate Key Handling:</strong></para>
/// Uses <c>ON CONFLICT DO NOTHING</c> to handle duplicate key inserts gracefully.
/// Concurrent inserts of the same key are idempotent and will not throw exceptions.
/// <para><strong>Performance:</strong></para>
/// Leverages stored functions for efficient operations and index utilization.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Function names are constructed from validated IdempotencyKeyOptions.Schema property, not user input."
)]
internal sealed class PostgreSqlIdempotencyKeyRepository : IIdempotencyKeyRepository
{
    /// <summary>The PostgreSQL connection string used to open new connections for each repository operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached SQL for checking if an idempotency key exists.</summary>
    private readonly string _existsSql;

    /// <summary>Cached SQL for inserting an idempotency key.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlIdempotencyKeyRepository"/> class.
    /// </summary>
    /// <param name="options">The idempotency key configuration options.</param>
    public PostgreSqlIdempotencyKeyRepository(IOptions<IdempotencyKeyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        _connectionString = options.Value.ConnectionString;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? IdempotencyKeySchema.DefaultSchema
            : options.Value.Schema;

        _existsSql = $"SELECT \"{schema}\".fn_exists_idempotency_key(@idempotency_key, @valid_from)";
        _insertSql = $"SELECT \"{schema}\".fn_insert_idempotency_key(@idempotency_key, @created_at)";
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string idempotencyKey,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        await using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(_existsSql, connection);

        _ = command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        _ = command.Parameters.AddWithValue("valid_from", validFrom.HasValue ? (object)validFrom.Value : DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
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
        await using var command = new NpgsqlCommand(_insertSql, connection);

        _ = command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        _ = command.Parameters.AddWithValue("created_at", createdAt);

        try
        {
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (IsDuplicateKeyException(ex))
        {
            // A concurrent request already stored the same key — this is idempotent and safe to ignore.
            // The function uses ON CONFLICT DO NOTHING which should handle this, but we catch it for safety.
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
    /// Determines whether the given exception was caused by a unique-constraint violation
    /// (i.e., a duplicate key insert).
    /// </summary>
    /// <remarks>
    /// PostgreSQL SQLSTATE <c>23505</c> indicates a unique-constraint violation.
    /// </remarks>
    private static bool IsDuplicateKeyException(PostgresException ex) =>
        ex.SqlState == PostgresErrorCodes.UniqueViolation;
}
