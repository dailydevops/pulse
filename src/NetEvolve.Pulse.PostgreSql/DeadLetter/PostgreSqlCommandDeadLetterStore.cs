namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of <see cref="ICommandDeadLetterStore"/> using ADO.NET.
/// Provides command dead letter persistence optimized for PostgreSQL.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/CommandDeadLetter.sql</c> to create the required
/// database objects before using this provider.
/// <para><strong>Duplicate Key Handling:</strong></para>
/// Catches unique-constraint violations on the primary key insert and silently ignores them,
/// matching the idempotent-store semantics used elsewhere in this provider.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "The table name is constructed from validated CommandDeadLetterOptions.Schema/TableName properties, not user input."
)]
internal sealed class PostgreSqlCommandDeadLetterStore : ICommandDeadLetterStore
{
    /// <summary>The PostgreSQL connection string used to open new connections for each operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached SQL for inserting a command dead letter entry.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlCommandDeadLetterStore"/> class.
    /// </summary>
    /// <param name="options">The command dead letter configuration options.</param>
    public PostgreSqlCommandDeadLetterStore(IOptions<CommandDeadLetterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        _connectionString = options.Value.ConnectionString;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? CommandDeadLetterSchema.DefaultSchema
            : options.Value.Schema;
        SqlIdentifier.Validate(schema, nameof(options.Value.Schema));

        var tableName = string.IsNullOrWhiteSpace(options.Value.TableName)
            ? CommandDeadLetterSchema.DefaultTableName
            : options.Value.TableName;
        SqlIdentifier.Validate(tableName, nameof(options.Value.TableName));

        var qualifiedTableName = $"\"{schema}\".\"{tableName}\"";

        _insertSql = $"""
            INSERT INTO {qualifiedTableName}
                ("{CommandDeadLetterSchema.Columns.Id}", "{CommandDeadLetterSchema.Columns.CommandType}", "{CommandDeadLetterSchema.Columns.Payload}", "{CommandDeadLetterSchema.Columns.ExceptionType}", "{CommandDeadLetterSchema.Columns.ExceptionMessage}", "{CommandDeadLetterSchema.Columns.OccurredAt}", "{CommandDeadLetterSchema.Columns.AttemptCount}", "{CommandDeadLetterSchema.Columns.Status}")
            VALUES
                (@id, @command_type, @payload, @exception_type, @exception_message, @occurred_at, @attempt_count, @status)
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
            var command = new NpgsqlCommand(_insertSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("id", Guid.NewGuid());
                _ = command.Parameters.AddWithValue("command_type", commandType);
                _ = command.Parameters.AddWithValue("payload", payload);
                _ = command.Parameters.AddWithValue(
                    "exception_type",
                    (object?)exception.GetType().AssemblyQualifiedName ?? DBNull.Value
                );
                _ = command.Parameters.AddWithValue("exception_message", (object?)exception.Message ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("occurred_at", DateTimeOffset.UtcNow);
                _ = command.Parameters.AddWithValue("attempt_count", 1);
                _ = command.Parameters.AddWithValue("status", (short)CommandDeadLetterStatus.New);

                try
                {
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (PostgresException ex) when (IsDuplicateKeyException(ex))
                {
                    // A concurrent request already stored an entry with the same identifier — this is
                    // extremely unlikely given a freshly generated Guid, but is handled defensively.
                }
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
