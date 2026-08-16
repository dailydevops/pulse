namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQLite implementation of <see cref="ICommandDeadLetterManagement"/> using ADO.NET.
/// Provides dead letter inspection, replay, dismissal, and statistics queries.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/003_CreateCommandDeadLetterTable.sql</c> to create the
/// required database objects before using this provider.
/// <para><strong>Replay:</strong></para>
/// Uses the shared <see cref="CommandDeadLetterReplayDispatcher"/> to resolve the command type and
/// dispatch it via <see cref="IMediatorSendOnly"/>.
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
internal sealed class SQLiteCommandDeadLetterManagement : ICommandDeadLetterManagement
{
    /// <summary>The SQLite connection string resolved from <see cref="CommandDeadLetterOptions"/>.</summary>
    private readonly string _connectionString;

    /// <summary>Whether to apply WAL journal mode to the database.</summary>
    private readonly bool _enableWalMode;

    /// <summary>Tracks whether the persistent WAL journal mode has already been applied to the database.</summary>
    private int _walModeApplied;

    /// <summary>The mediator used to dispatch replayed commands.</summary>
    private readonly IMediatorSendOnly _mediator;

    /// <summary>The serializer used to deserialize stored payloads for replay.</summary>
    private readonly IPayloadSerializer _payloadSerializer;

    // Cached SQL statements
    private readonly string _getPendingSql;
    private readonly string _getByIdSql;
    private readonly string _setReplayingSql;
    private readonly string _setResolvedSql;
    private readonly string _dismissSql;
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteCommandDeadLetterManagement"/> class.
    /// </summary>
    /// <param name="options">The command dead letter configuration options.</param>
    /// <param name="mediator">The mediator used to dispatch replayed commands.</param>
    /// <param name="payloadSerializer">The serializer used to deserialize stored payloads for replay.</param>
    public SQLiteCommandDeadLetterManagement(
        IOptions<CommandDeadLetterOptions> options,
        IMediatorSendOnly mediator,
        IPayloadSerializer payloadSerializer
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;
        _enableWalMode = opts.EnableWalMode;
        _mediator = mediator;
        _payloadSerializer = payloadSerializer;

        SqlIdentifier.Validate(opts.TableName, nameof(opts.TableName));
        var table = $"\"{opts.TableName}\"";

        _getPendingSql = $"""
            SELECT
                "{CommandDeadLetterSchema.Columns.Id}",
                "{CommandDeadLetterSchema.Columns.CommandType}",
                "{CommandDeadLetterSchema.Columns.Payload}",
                "{CommandDeadLetterSchema.Columns.ExceptionType}",
                "{CommandDeadLetterSchema.Columns.ExceptionMessage}",
                "{CommandDeadLetterSchema.Columns.OccurredAt}",
                "{CommandDeadLetterSchema.Columns.AttemptCount}",
                "{CommandDeadLetterSchema.Columns.Status}"
            FROM {table}
            WHERE "{CommandDeadLetterSchema.Columns.Status}" = 0
            ORDER BY "{CommandDeadLetterSchema.Columns.OccurredAt}" ASC
            LIMIT @count;
            """;

        _getByIdSql = $"""
            SELECT
                "{CommandDeadLetterSchema.Columns.Id}",
                "{CommandDeadLetterSchema.Columns.CommandType}",
                "{CommandDeadLetterSchema.Columns.Payload}",
                "{CommandDeadLetterSchema.Columns.ExceptionType}",
                "{CommandDeadLetterSchema.Columns.ExceptionMessage}",
                "{CommandDeadLetterSchema.Columns.OccurredAt}",
                "{CommandDeadLetterSchema.Columns.AttemptCount}",
                "{CommandDeadLetterSchema.Columns.Status}"
            FROM {table}
            WHERE "{CommandDeadLetterSchema.Columns.Id}" = @id;
            """;

        _setReplayingSql = $"""
            UPDATE {table}
            SET "{CommandDeadLetterSchema.Columns.Status}" = 1
            WHERE "{CommandDeadLetterSchema.Columns.Id}" = @id;
            """;

        _setResolvedSql = $"""
            UPDATE {table}
            SET "{CommandDeadLetterSchema.Columns.Status}" = 2
            WHERE "{CommandDeadLetterSchema.Columns.Id}" = @id;
            """;

        _dismissSql = $"""
            UPDATE {table}
            SET "{CommandDeadLetterSchema.Columns.Status}" = 3
            WHERE "{CommandDeadLetterSchema.Columns.Id}" = @id;
            """;

        _getStatisticsSql = $"""
            SELECT "{CommandDeadLetterSchema.Columns.Status}", COUNT(*)
            FROM {table}
            GROUP BY "{CommandDeadLetterSchema.Columns.Status}";
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
            var command = new SqliteCommand(_getPendingSql, connection);
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

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var entry =
                await GetByIdAsync(connection, id, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"CommandDeadLetterEntry '{id}' was not found.");

            var replayingCommand = new SqliteCommand(_setReplayingSql, connection);
            await using (replayingCommand.ConfigureAwait(false))
            {
                _ = replayingCommand.Parameters.AddWithValue("@id", id.ToString());
                _ = await replayingCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await CommandDeadLetterReplayDispatcher
                .ReplayAsync(_mediator, _payloadSerializer, entry.CommandType, entry.Payload, cancellationToken)
                .ConfigureAwait(false);

            var resolvedCommand = new SqliteCommand(_setResolvedSql, connection);
            await using (resolvedCommand.ConfigureAwait(false))
            {
                _ = resolvedCommand.Parameters.AddWithValue("@id", id.ToString());
                _ = await resolvedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task DismissAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new SqliteCommand(_dismissSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@id", id.ToString());

                var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (updated == 0)
                {
                    throw new KeyNotFoundException($"CommandDeadLetterEntry '{id}' was not found.");
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
            var command = new SqliteCommand(_getStatisticsSql, connection);
            await using (command.ConfigureAwait(false))
            {
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var status = (CommandDeadLetterStatus)reader.GetInt64(0);
                        var count = (int)reader.GetInt64(1);

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
    /// Retrieves a single <see cref="CommandDeadLetterEntry"/> by its identifier on the given connection.
    /// </summary>
    /// <param name="connection">The open <see cref="SqliteConnection"/> to use.</param>
    /// <param name="id">The identifier of the entry to retrieve.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The matching <see cref="CommandDeadLetterEntry"/>, or <see langword="null"/> if not found.</returns>
    private async Task<CommandDeadLetterEntry?> GetByIdAsync(
        SqliteConnection connection,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = new SqliteCommand(_getByIdSql, connection);
        await using (command.ConfigureAwait(false))
        {
            _ = command.Parameters.AddWithValue("@id", id.ToString());

            var entries = await ReadEntriesAsync(command, cancellationToken).ConfigureAwait(false);
            return entries.Count > 0 ? entries[0] : null;
        }
    }

    /// <summary>
    /// Opens and returns a new <see cref="SqliteConnection"/> using the stored connection string.
    /// Applies WAL mode once per instance when <see cref="CommandDeadLetterOptions.EnableWalMode"/> is
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

    /// <summary>
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="CommandDeadLetterEntry"/> instances.
    /// </summary>
    /// <param name="command">The <see cref="SqliteCommand"/> to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of <see cref="CommandDeadLetterEntry"/> records.</returns>
    private static async Task<IReadOnlyList<CommandDeadLetterEntry>> ReadEntriesAsync(
        SqliteCommand command,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entries = new List<CommandDeadLetterEntry>();

        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return entries;
            }

            var ordId = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Id);
            var ordCommandType = reader.GetOrdinal(CommandDeadLetterSchema.Columns.CommandType);
            var ordPayload = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Payload);
            var ordExceptionType = reader.GetOrdinal(CommandDeadLetterSchema.Columns.ExceptionType);
            var ordExceptionMessage = reader.GetOrdinal(CommandDeadLetterSchema.Columns.ExceptionMessage);
            var ordOccurredAt = reader.GetOrdinal(CommandDeadLetterSchema.Columns.OccurredAt);
            var ordAttemptCount = reader.GetOrdinal(CommandDeadLetterSchema.Columns.AttemptCount);
            var ordStatus = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Status);

            do
            {
                var exceptionTypeNull = await reader
                    .IsDBNullAsync(ordExceptionType, cancellationToken)
                    .ConfigureAwait(false);
                var exceptionMessageNull = await reader
                    .IsDBNullAsync(ordExceptionMessage, cancellationToken)
                    .ConfigureAwait(false);
                var occurredAt = await reader
                    .GetFieldValueAsync<DateTimeOffset>(ordOccurredAt, cancellationToken)
                    .ConfigureAwait(false);

                entries.Add(
                    new CommandDeadLetterEntry
                    {
                        Id = Guid.Parse(reader.GetString(ordId)),
                        CommandType = reader.GetString(ordCommandType),
                        Payload = reader.GetString(ordPayload),
                        ExceptionType = exceptionTypeNull ? null : reader.GetString(ordExceptionType),
                        ExceptionMessage = exceptionMessageNull ? null : reader.GetString(ordExceptionMessage),
                        OccurredAt = occurredAt,
                        AttemptCount = (int)reader.GetInt64(ordAttemptCount),
                        Status = (CommandDeadLetterStatus)reader.GetInt64(ordStatus),
                    }
                );
            } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));

            return entries;
        }
    }
}
