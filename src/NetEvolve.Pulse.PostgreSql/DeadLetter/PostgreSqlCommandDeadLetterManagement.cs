namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of <see cref="ICommandDeadLetterManagement"/> using ADO.NET.
/// Provides dead-letter inspection, replay, dismissal, and statistics queries for PostgreSQL.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/CommandDeadLetter.sql</c> to create the required
/// database objects before using this provider.
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
internal sealed class PostgreSqlCommandDeadLetterManagement : ICommandDeadLetterManagement
{
    /// <summary>The PostgreSQL connection string used to open new connections for each operation.</summary>
    private readonly string _connectionString;

    /// <summary>The mediator used to dispatch replayed commands.</summary>
    private readonly IMediatorSendOnly _mediator;

    /// <summary>The serializer used to deserialize stored payloads for replay.</summary>
    private readonly IPayloadSerializer _payloadSerializer;

    /// <summary>Cached SQL for selecting pending dead letter entries.</summary>
    private readonly string _getPendingSql;

    /// <summary>Cached SQL for selecting a single dead letter entry by identifier.</summary>
    private readonly string _getByIdSql;

    /// <summary>Cached SQL for updating the status of a dead letter entry.</summary>
    private readonly string _updateStatusSql;

    /// <summary>Cached SQL for aggregating entry counts per status.</summary>
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlCommandDeadLetterManagement"/> class.
    /// </summary>
    /// <param name="options">The command dead letter configuration options.</param>
    /// <param name="mediator">The mediator used to dispatch replayed commands.</param>
    /// <param name="payloadSerializer">The serializer used to deserialize stored payloads for replay.</param>
    public PostgreSqlCommandDeadLetterManagement(
        IOptions<CommandDeadLetterOptions> options,
        IMediatorSendOnly mediator,
        IPayloadSerializer payloadSerializer
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(payloadSerializer);

        _connectionString = options.Value.ConnectionString;
        _mediator = mediator;
        _payloadSerializer = payloadSerializer;

        var schema = string.IsNullOrWhiteSpace(options.Value.Schema)
            ? CommandDeadLetterSchema.DefaultSchema
            : options.Value.Schema;
        SqlIdentifier.Validate(schema, nameof(options.Value.Schema));

        var tableName = string.IsNullOrWhiteSpace(options.Value.TableName)
            ? CommandDeadLetterSchema.DefaultTableName
            : options.Value.TableName;
        SqlIdentifier.Validate(tableName, nameof(options.Value.TableName));

        var qualifiedTableName = $"\"{schema}\".\"{tableName}\"";

        var columns =
            $"\"{CommandDeadLetterSchema.Columns.Id}\", \"{CommandDeadLetterSchema.Columns.CommandType}\", "
            + $"\"{CommandDeadLetterSchema.Columns.Payload}\", \"{CommandDeadLetterSchema.Columns.ExceptionType}\", "
            + $"\"{CommandDeadLetterSchema.Columns.ExceptionMessage}\", \"{CommandDeadLetterSchema.Columns.OccurredAt}\", "
            + $"\"{CommandDeadLetterSchema.Columns.AttemptCount}\", \"{CommandDeadLetterSchema.Columns.Status}\"";

        _getPendingSql = $"""
            SELECT {columns}
            FROM {qualifiedTableName}
            WHERE "{CommandDeadLetterSchema.Columns.Status}" = @status
            ORDER BY "{CommandDeadLetterSchema.Columns.OccurredAt}" ASC
            LIMIT @count
            """;

        _getByIdSql = $"""
            SELECT {columns}
            FROM {qualifiedTableName}
            WHERE "{CommandDeadLetterSchema.Columns.Id}" = @id
            """;

        _updateStatusSql = $"""
            UPDATE {qualifiedTableName}
            SET "{CommandDeadLetterSchema.Columns.Status}" = @status
            WHERE "{CommandDeadLetterSchema.Columns.Id}" = @id
            """;

        _getStatisticsSql = $"""
            SELECT "{CommandDeadLetterSchema.Columns.Status}", COUNT(*)
            FROM {qualifiedTableName}
            GROUP BY "{CommandDeadLetterSchema.Columns.Status}"
            """;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommandDeadLetterEntry>> GetPendingAsync(
        int count = 50,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(_getPendingSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("status", (short)CommandDeadLetterStatus.New);
                _ = command.Parameters.AddWithValue("count", count);

                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    var entries = new List<CommandDeadLetterEntry>();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        entries.Add(MapToEntry(reader));
                    }

                    return entries;
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task ReplayAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var entry =
                await GetByIdAsync(connection, id, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"CommandDeadLetterEntry '{id}' was not found.");

            await UpdateStatusAsync(connection, id, CommandDeadLetterStatus.Replaying, cancellationToken)
                .ConfigureAwait(false);

            await CommandDeadLetterReplayDispatcher
                .ReplayAsync(_mediator, _payloadSerializer, entry.CommandType, entry.Payload, cancellationToken)
                .ConfigureAwait(false);

            await UpdateStatusAsync(connection, id, CommandDeadLetterStatus.Resolved, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DismissAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(_updateStatusSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("status", (short)CommandDeadLetterStatus.Dismissed);
                _ = command.Parameters.AddWithValue("id", id);

                var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (affected == 0)
                {
                    throw new KeyNotFoundException($"CommandDeadLetterEntry '{id}' was not found.");
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<CommandDeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(_getStatisticsSql, connection);
            await using (command.ConfigureAwait(false))
            {
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    var newCount = 0;
                    var replayingCount = 0;
                    var resolvedCount = 0;
                    var dismissedCount = 0;

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var status = (CommandDeadLetterStatus)reader.GetInt16(0);
                        var groupCount = Convert.ToInt32(
                            reader.GetInt64(1),
                            System.Globalization.CultureInfo.InvariantCulture
                        );

                        switch (status)
                        {
                            case CommandDeadLetterStatus.New:
                                newCount = groupCount;
                                break;
                            case CommandDeadLetterStatus.Replaying:
                                replayingCount = groupCount;
                                break;
                            case CommandDeadLetterStatus.Resolved:
                                resolvedCount = groupCount;
                                break;
                            case CommandDeadLetterStatus.Dismissed:
                                dismissedCount = groupCount;
                                break;
                        }
                    }

                    return new CommandDeadLetterStatistics(newCount, replayingCount, resolvedCount, dismissedCount);
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
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Retrieves the dead letter entry identified by <paramref name="id"/>, or <see langword="null"/>
    /// when no such entry exists.
    /// </summary>
    private async Task<CommandDeadLetterEntry?> GetByIdAsync(
        NpgsqlConnection connection,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var command = new NpgsqlCommand(_getByIdSql, connection);
        await using (command.ConfigureAwait(false))
        {
            _ = command.Parameters.AddWithValue("id", id);

            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                return MapToEntry(reader);
            }
        }
    }

    /// <summary>
    /// Updates the status of the dead letter entry identified by <paramref name="id"/>.
    /// </summary>
    private async Task UpdateStatusAsync(
        NpgsqlConnection connection,
        Guid id,
        CommandDeadLetterStatus status,
        CancellationToken cancellationToken
    )
    {
        var command = new NpgsqlCommand(_updateStatusSql, connection);
        await using (command.ConfigureAwait(false))
        {
            _ = command.Parameters.AddWithValue("status", (short)status);
            _ = command.Parameters.AddWithValue("id", id);

            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Maps the current row of a <see cref="NpgsqlDataReader"/> to a new <see cref="CommandDeadLetterEntry"/> instance.
    /// </summary>
    private static CommandDeadLetterEntry MapToEntry(NpgsqlDataReader reader)
    {
        var ordId = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Id);
        var ordCommandType = reader.GetOrdinal(CommandDeadLetterSchema.Columns.CommandType);
        var ordPayload = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Payload);
        var ordExceptionType = reader.GetOrdinal(CommandDeadLetterSchema.Columns.ExceptionType);
        var ordExceptionMessage = reader.GetOrdinal(CommandDeadLetterSchema.Columns.ExceptionMessage);
        var ordOccurredAt = reader.GetOrdinal(CommandDeadLetterSchema.Columns.OccurredAt);
        var ordAttemptCount = reader.GetOrdinal(CommandDeadLetterSchema.Columns.AttemptCount);
        var ordStatus = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Status);

        return new CommandDeadLetterEntry
        {
            Id = reader.GetGuid(ordId),
            CommandType = reader.GetString(ordCommandType),
            Payload = reader.GetString(ordPayload),
            ExceptionType = reader.IsDBNull(ordExceptionType) ? null : reader.GetString(ordExceptionType),
            ExceptionMessage = reader.IsDBNull(ordExceptionMessage) ? null : reader.GetString(ordExceptionMessage),
            OccurredAt = reader.GetFieldValue<DateTimeOffset>(ordOccurredAt),
            AttemptCount = reader.GetInt32(ordAttemptCount),
            Status = (CommandDeadLetterStatus)reader.GetInt16(ordStatus),
        };
    }
}
