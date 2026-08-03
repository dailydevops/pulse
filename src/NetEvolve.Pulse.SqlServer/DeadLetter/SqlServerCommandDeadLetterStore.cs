namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQL Server implementation of <see cref="ICommandDeadLetterStore"/> using ADO.NET.
/// Provides command dead letter persistence optimized for SQL Server.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/CommandDeadLetter.sql</c> to create the required
/// database objects before using this provider.
/// <para><strong>Duplicate Key Handling:</strong></para>
/// Wraps the insert in a try/catch for unique-constraint/primary-key violations, since the
/// dead letter entry <see cref="CommandDeadLetterEntry.Id"/> is a freshly generated <see cref="Guid"/>
/// and a duplicate is only possible under extremely unlikely concurrent collisions.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "The SQL command text is constructed from validated CommandDeadLetterOptions.Schema/TableName properties, not user input."
)]
internal sealed class SqlServerCommandDeadLetterStore : ICommandDeadLetterStore
{
    /// <summary>The SQL Server connection string used to open new connections for each store operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached SQL command text for inserting a new command dead letter entry.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerCommandDeadLetterStore"/> class.
    /// </summary>
    /// <param name="options">The command dead letter configuration options.</param>
    public SqlServerCommandDeadLetterStore(IOptions<CommandDeadLetterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        _connectionString = options.Value.ConnectionString;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? CommandDeadLetterSchema.DefaultSchema
            : options.Value.Schema;
        SqlIdentifier.Validate(schema, nameof(options.Value.Schema));
        SqlIdentifier.Validate(options.Value.TableName, nameof(options.Value.TableName));

        var fullTableName = $"[{schema}].[{options.Value.TableName}]";

        _insertSql = $"""
            INSERT INTO {fullTableName}
                ([{CommandDeadLetterSchema.Columns.Id}],
                 [{CommandDeadLetterSchema.Columns.CommandType}],
                 [{CommandDeadLetterSchema.Columns.Payload}],
                 [{CommandDeadLetterSchema.Columns.ExceptionType}],
                 [{CommandDeadLetterSchema.Columns.ExceptionMessage}],
                 [{CommandDeadLetterSchema.Columns.OccurredAt}],
                 [{CommandDeadLetterSchema.Columns.AttemptCount}],
                 [{CommandDeadLetterSchema.Columns.Status}])
            VALUES
                (@Id, @CommandType, @Payload, @ExceptionType, @ExceptionMessage, @OccurredAt, @AttemptCount, @Status)
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
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentNullException.ThrowIfNull(exception);

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new SqlCommand(_insertSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@Id", Guid.NewGuid());
                _ = command.Parameters.AddWithValue("@CommandType", commandType);
                _ = command.Parameters.AddWithValue("@Payload", payload);
                _ = command.Parameters.AddWithValue(
                    "@ExceptionType",
                    (object?)exception.GetType().AssemblyQualifiedName ?? DBNull.Value
                );
                _ = command.Parameters.AddWithValue("@ExceptionMessage", (object?)exception.Message ?? DBNull.Value);
                _ = command.Parameters.AddWithValue("@OccurredAt", DateTimeOffset.UtcNow);
                _ = command.Parameters.AddWithValue("@AttemptCount", 1);
                _ = command.Parameters.AddWithValue("@Status", (short)CommandDeadLetterStatus.New);

                try
                {
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (SqlException ex) when (IsDuplicateKeyException(ex))
                {
                    // A concurrent insert already used the same identifier — extremely unlikely for a
                    // freshly generated Guid, but treated as idempotent and safe to ignore for safety.
                }
            }
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
