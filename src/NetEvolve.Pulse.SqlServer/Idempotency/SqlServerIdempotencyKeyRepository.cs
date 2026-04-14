namespace NetEvolve.Pulse.Idempotency;

using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// SQL Server implementation of <see cref="IIdempotencyKeyRepository"/> using ADO.NET.
/// Provides idempotency key persistence optimized for SQL Server.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/IdempotencyKey.sql</c> to create the required
/// database objects before using this provider.
/// <para><strong>Duplicate Key Handling:</strong></para>
/// Uses stored procedures with MERGE statement to handle duplicate key inserts gracefully.
/// Concurrent inserts of the same key are idempotent and will not throw exceptions.
/// <para><strong>Performance:</strong></para>
/// Leverages stored procedures for efficient operations and index utilization.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Stored procedure names are constructed from validated IdempotencyKeyOptions.Schema property, not user input."
)]
internal sealed class SqlServerIdempotencyKeyRepository : IIdempotencyKeyRepository
{
    /// <summary>The SQL Server connection string used to open new connections for each repository operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached stored procedure name for checking if an idempotency key exists.</summary>
    private readonly string _existsSql;

    /// <summary>Cached stored procedure name for inserting an idempotency key.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerIdempotencyKeyRepository"/> class.
    /// </summary>
    /// <param name="options">The idempotency key configuration options.</param>
    public SqlServerIdempotencyKeyRepository(IOptions<IdempotencyKeyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        _connectionString = options.Value.ConnectionString;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? IdempotencyKeySchema.DefaultSchema
            : options.Value.Schema;

        _existsSql = $"[{schema}].[usp_ExistsIdempotencyKey]";
        _insertSql = $"[{schema}].[usp_InsertIdempotencyKey]";
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
        await using var command = new SqlCommand(_existsSql, connection) { CommandType = CommandType.StoredProcedure };

        _ = command.Parameters.Add(
            new SqlParameter("@idempotencyKey", SqlDbType.NVarChar, 500) { Value = idempotencyKey }
        );

        if (validFrom.HasValue)
        {
            _ = command.Parameters.Add(
                new SqlParameter("@validFrom", SqlDbType.DateTimeOffset) { Value = validFrom.Value }
            );
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is bool exists && exists;
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
        await using var command = new SqlCommand(_insertSql, connection) { CommandType = CommandType.StoredProcedure };

        _ = command.Parameters.Add(
            new SqlParameter("@idempotencyKey", SqlDbType.NVarChar, 500) { Value = idempotencyKey }
        );
        _ = command.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.DateTimeOffset) { Value = createdAt });

        try
        {
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex) when (IsDuplicateKeyException(ex))
        {
            // A concurrent request already stored the same key — this is idempotent and safe to ignore.
            // The stored procedure uses MERGE which should handle this, but we catch it for safety.
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

    /// <summary>
    /// Determines whether the given exception was caused by a unique-constraint or
    /// primary-key violation (i.e., a duplicate key insert).
    /// </summary>
    /// <remarks>
    /// SQL Server / Azure SQL:
    /// <list type="bullet">
    /// <item><description>Error 2627 (PK violation): "Violation of PRIMARY KEY constraint '...'. Cannot insert duplicate key ..."</description></item>
    /// <item><description>Error 2601 (unique-ix violation): "Cannot insert duplicate key row in object '...' with unique index '...'"</description></item>
    /// </list>
    /// </remarks>
    private static bool IsDuplicateKeyException(SqlException ex) => ex.Number is 2627 or 2601;
}
