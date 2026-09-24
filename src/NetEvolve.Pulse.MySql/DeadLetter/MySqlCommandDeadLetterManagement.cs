namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// MySQL implementation of <see cref="ICommandDeadLetterManagement"/> using ADO.NET.
/// Provides pending inspection, replay, dismissal, and statistics queries for the command dead letter store.
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
internal sealed class MySqlCommandDeadLetterManagement : ICommandDeadLetterManagement
{
    private readonly string _connectionString;
    private readonly IMediatorSendOnly _mediator;
    private readonly IPayloadSerializer _payloadSerializer;

    // Cached SQL statements
    private readonly string _getPendingSql;
    private readonly string _getByIdSql;
    private readonly string _markReplayingSql;
    private readonly string _markResolvedSql;
    private readonly string _markDismissedSql;
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlCommandDeadLetterManagement"/> class.
    /// </summary>
    /// <param name="options">The command dead letter configuration options.</param>
    /// <param name="mediator">The mediator used to dispatch replayed commands.</param>
    /// <param name="payloadSerializer">The serializer used to deserialize stored payloads.</param>
    public MySqlCommandDeadLetterManagement(
        IOptions<CommandDeadLetterOptions> options,
        IMediatorSendOnly mediator,
        IPayloadSerializer payloadSerializer
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(payloadSerializer);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;
        _mediator = mediator;
        _payloadSerializer = payloadSerializer;

        SqlIdentifier.Validate(opts.TableName, nameof(opts.TableName));
        var table = $"`{opts.TableName}`";

        _getPendingSql = $"""
            SELECT
                `{CommandDeadLetterSchema.Columns.Id}`,
                `{CommandDeadLetterSchema.Columns.CommandType}`,
                `{CommandDeadLetterSchema.Columns.Payload}`,
                `{CommandDeadLetterSchema.Columns.ExceptionType}`,
                `{CommandDeadLetterSchema.Columns.ExceptionMessage}`,
                `{CommandDeadLetterSchema.Columns.OccurredAt}`,
                `{CommandDeadLetterSchema.Columns.AttemptCount}`,
                `{CommandDeadLetterSchema.Columns.Status}`
            FROM {table}
            WHERE `{CommandDeadLetterSchema.Columns.Status}` = 0
            ORDER BY `{CommandDeadLetterSchema.Columns.OccurredAt}` ASC
            LIMIT @count
            """;

        _getByIdSql = $"""
            SELECT
                `{CommandDeadLetterSchema.Columns.Id}`,
                `{CommandDeadLetterSchema.Columns.CommandType}`,
                `{CommandDeadLetterSchema.Columns.Payload}`,
                `{CommandDeadLetterSchema.Columns.ExceptionType}`,
                `{CommandDeadLetterSchema.Columns.ExceptionMessage}`,
                `{CommandDeadLetterSchema.Columns.OccurredAt}`,
                `{CommandDeadLetterSchema.Columns.AttemptCount}`,
                `{CommandDeadLetterSchema.Columns.Status}`
            FROM {table}
            WHERE `{CommandDeadLetterSchema.Columns.Id}` = @id
            """;

        _markReplayingSql = $"""
            UPDATE {table}
            SET `{CommandDeadLetterSchema.Columns.Status}` = 1
            WHERE `{CommandDeadLetterSchema.Columns.Id}` = @id
            """;

        _markResolvedSql = $"""
            UPDATE {table}
            SET `{CommandDeadLetterSchema.Columns.Status}` = 2
            WHERE `{CommandDeadLetterSchema.Columns.Id}` = @id
            """;

        _markDismissedSql = $"""
            UPDATE {table}
            SET `{CommandDeadLetterSchema.Columns.Status}` = 3
            WHERE `{CommandDeadLetterSchema.Columns.Id}` = @id
            """;

        _getStatisticsSql = $"""
            SELECT `{CommandDeadLetterSchema.Columns.Status}`, COUNT(*)
            FROM {table}
            GROUP BY `{CommandDeadLetterSchema.Columns.Status}`
            """;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommandDeadLetterEntry>> GetPendingAsync(
        int count = 50,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new MySqlCommand(_getPendingSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@count", count);

                return await ReadEntriesAsync(command, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task ReplayAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false) ?? throw NotFound(id);

        await SetStatusAsync(_markReplayingSql, id, cancellationToken).ConfigureAwait(false);

        await CommandDeadLetterReplayDispatcher
            .ReplayAsync(_mediator, _payloadSerializer, entry.CommandType, entry.Payload, cancellationToken)
            .ConfigureAwait(false);

        await SetStatusAsync(_markResolvedSql, id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DismissAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new MySqlCommand(_markDismissedSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@id", id.ToByteArray());

                var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (affected == 0)
                {
                    throw NotFound(id);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<CommandDeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var newCount = 0;
        var replayingCount = 0;
        var resolvedCount = 0;
        var dismissedCount = 0;

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new MySqlCommand(_getStatisticsSql, connection);
            await using (command.ConfigureAwait(false))
            {
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var status = (CommandDeadLetterStatus)reader.GetInt32(0);
                        var count = Convert.ToInt32(
                            reader.GetValue(1),
                            System.Globalization.CultureInfo.InvariantCulture
                        );

                        switch (status)
                        {
                            case CommandDeadLetterStatus.New:
                                newCount = count;
                                break;
                            case CommandDeadLetterStatus.Replaying:
                                replayingCount = count;
                                break;
                            case CommandDeadLetterStatus.Resolved:
                                resolvedCount = count;
                                break;
                            case CommandDeadLetterStatus.Dismissed:
                                dismissedCount = count;
                                break;
                        }
                    }
                }
            }
        }

        return new CommandDeadLetterStatistics(newCount, replayingCount, resolvedCount, dismissedCount);
    }

    /// <summary>
    /// Retrieves the dead letter entry identified by <paramref name="id"/>, or <see langword="null"/> when not found.
    /// </summary>
    private async Task<CommandDeadLetterEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new MySqlCommand(_getByIdSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@id", id.ToByteArray());

                var entries = await ReadEntriesAsync(command, cancellationToken).ConfigureAwait(false);
                return entries.Count > 0 ? entries[0] : null;
            }
        }
    }

    /// <summary>
    /// Executes a status-update statement parameterized on <c>@id</c> against the dead letter entry
    /// identified by <paramref name="id"/>.
    /// </summary>
    private async Task SetStatusAsync(string sql, Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new MySqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@id", id.ToByteArray());

                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Opens and returns a new <see cref="MySqlConnection"/> using the stored connection string.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    private async Task<MySqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="CommandDeadLetterEntry"/> instances.
    /// </summary>
    private static async Task<IReadOnlyList<CommandDeadLetterEntry>> ReadEntriesAsync(
        MySqlCommand command,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return [];
            }

            var ordId = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Id);
            var ordCommandType = reader.GetOrdinal(CommandDeadLetterSchema.Columns.CommandType);
            var ordPayload = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Payload);
            var ordExceptionType = reader.GetOrdinal(CommandDeadLetterSchema.Columns.ExceptionType);
            var ordExceptionMessage = reader.GetOrdinal(CommandDeadLetterSchema.Columns.ExceptionMessage);
            var ordOccurredAt = reader.GetOrdinal(CommandDeadLetterSchema.Columns.OccurredAt);
            var ordAttemptCount = reader.GetOrdinal(CommandDeadLetterSchema.Columns.AttemptCount);
            var ordStatus = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Status);

            var entries = new List<CommandDeadLetterEntry>();
            do
            {
                var idBytes = await reader.GetFieldValueAsync<byte[]>(ordId, cancellationToken).ConfigureAwait(false);
                var exceptionTypeNull = await reader
                    .IsDBNullAsync(ordExceptionType, cancellationToken)
                    .ConfigureAwait(false);
                var exceptionMessageNull = await reader
                    .IsDBNullAsync(ordExceptionMessage, cancellationToken)
                    .ConfigureAwait(false);

                entries.Add(
                    new CommandDeadLetterEntry
                    {
                        Id = new Guid(idBytes),
                        CommandType = reader.GetString(ordCommandType),
                        Payload = reader.GetString(ordPayload),
                        ExceptionType = exceptionTypeNull ? null : reader.GetString(ordExceptionType),
                        ExceptionMessage = exceptionMessageNull ? null : reader.GetString(ordExceptionMessage),
                        OccurredAt = new DateTimeOffset(reader.GetInt64(ordOccurredAt), TimeSpan.Zero),
                        AttemptCount = reader.GetInt32(ordAttemptCount),
                        Status = (CommandDeadLetterStatus)reader.GetInt32(ordStatus),
                    }
                );
            } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));

            return entries;
        }
    }

    /// <summary>
    /// Creates the exception thrown when a dead letter entry cannot be found by its identifier.
    /// </summary>
    private static KeyNotFoundException NotFound(Guid id) => new($"CommandDeadLetterEntry '{id}' was not found.");
}
