namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// MySQL implementation of <see cref="ICommandDeadLetterStore"/> using ADO.NET.
/// Provides command dead letter persistence optimized for MySQL.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/CommandDeadLetter.sql</c> to create the required
/// database objects before using this provider.
/// <para><strong>Schema:</strong></para>
/// MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
/// All tables reside in the active database specified by the connection string.
/// The <see cref="CommandDeadLetterOptions.Schema"/> property is ignored for MySQL.
/// <para><strong>Timestamps:</strong></para>
/// Stores <see cref="DateTimeOffset"/> values as <c>BIGINT</c> (UTC ticks), matching the
/// interoperability contract with the Entity Framework MySQL provider.
/// <para><strong>Duplicate Key Handling:</strong></para>
/// Uses <c>INSERT IGNORE</c> so a duplicate <see cref="CommandDeadLetterEntry.Id"/> (an extremely
/// unlikely freshly-generated <see cref="Guid"/> collision) is silently ignored rather than throwing.
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
internal sealed class MySqlCommandDeadLetterStore : ICommandDeadLetterStore
{
    /// <summary>The MySQL connection string used to open new connections for each store operation.</summary>
    private readonly string _connectionString;

    /// <summary>Cached SQL statement for inserting a command dead letter entry.</summary>
    private readonly string _insertSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlCommandDeadLetterStore"/> class.
    /// </summary>
    /// <param name="options">The command dead letter configuration options.</param>
    public MySqlCommandDeadLetterStore(IOptions<CommandDeadLetterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;

        SqlIdentifier.Validate(opts.TableName, nameof(opts.TableName));
        var table = $"`{opts.TableName}`";

        _insertSql = $"""
            INSERT IGNORE INTO {table}
                (`{CommandDeadLetterSchema.Columns.Id}`,
                 `{CommandDeadLetterSchema.Columns.CommandType}`,
                 `{CommandDeadLetterSchema.Columns.Payload}`,
                 `{CommandDeadLetterSchema.Columns.ExceptionType}`,
                 `{CommandDeadLetterSchema.Columns.ExceptionMessage}`,
                 `{CommandDeadLetterSchema.Columns.OccurredAt}`,
                 `{CommandDeadLetterSchema.Columns.AttemptCount}`,
                 `{CommandDeadLetterSchema.Columns.Status}`)
            VALUES
                (@id, @commandType, @payload, @exceptionType, @exceptionMessage, @occurredAtTicks, @attemptCount, @status)
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
            var command = new MySqlCommand(_insertSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@id", Guid.NewGuid().ToByteArray());
                _ = command.Parameters.AddWithValue("@commandType", commandType);
                _ = command.Parameters.AddWithValue("@payload", payload);
                _ = command.Parameters.AddWithValue("@exceptionType", exception.GetType().AssemblyQualifiedName);
                _ = command.Parameters.AddWithValue("@exceptionMessage", exception.Message);
                _ = command.Parameters.AddWithValue("@occurredAtTicks", DateTimeOffset.UtcNow.UtcTicks);
                _ = command.Parameters.AddWithValue("@attemptCount", 1);
                _ = command.Parameters.AddWithValue("@status", (int)CommandDeadLetterStatus.New);

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
