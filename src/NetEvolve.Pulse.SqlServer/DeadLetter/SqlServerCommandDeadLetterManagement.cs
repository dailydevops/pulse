namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// SQL Server implementation of <see cref="ICommandDeadLetterManagement"/> using ADO.NET.
/// Provides pending-entry inspection, replay, dismissal, and statistics queries for the command dead letter store.
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
    Justification = "The SQL command text is constructed from validated CommandDeadLetterOptions.Schema/TableName properties, not user input."
)]
internal sealed class SqlServerCommandDeadLetterManagement : ICommandDeadLetterManagement
{
    /// <summary>The SQL Server connection string used to open new connections for each management operation.</summary>
    private readonly string _connectionString;

    /// <summary>The mediator used to dispatch replayed commands.</summary>
    private readonly IMediatorSendOnly _mediator;

    /// <summary>The serializer used to deserialize stored payloads for replay.</summary>
    private readonly IPayloadSerializer _payloadSerializer;

    /// <summary>Cached SQL command text for retrieving pending dead letter entries.</summary>
    private readonly string _getPendingSql;

    /// <summary>Cached SQL command text for retrieving a single dead letter entry by identifier.</summary>
    private readonly string _getByIdSql;

    /// <summary>Cached SQL command text for updating the status of a dead letter entry.</summary>
    private readonly string _updateStatusSql;

    /// <summary>Cached SQL command text for retrieving aggregate status counts.</summary>
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerCommandDeadLetterManagement"/> class.
    /// </summary>
    /// <param name="options">The command dead letter configuration options.</param>
    /// <param name="mediator">The mediator used to dispatch replayed commands.</param>
    /// <param name="payloadSerializer">The serializer used to deserialize stored payloads.</param>
    public SqlServerCommandDeadLetterManagement(
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
        SqlIdentifier.Validate(options.Value.TableName, nameof(options.Value.TableName));

        var fullTableName = $"[{schema}].[{options.Value.TableName}]";

        _getPendingSql = $"""
            SELECT TOP (@count)
                   [{CommandDeadLetterSchema.Columns.Id}],
                   [{CommandDeadLetterSchema.Columns.CommandType}],
                   [{CommandDeadLetterSchema.Columns.Payload}],
                   [{CommandDeadLetterSchema.Columns.ExceptionType}],
                   [{CommandDeadLetterSchema.Columns.ExceptionMessage}],
                   [{CommandDeadLetterSchema.Columns.OccurredAt}],
                   [{CommandDeadLetterSchema.Columns.AttemptCount}],
                   [{CommandDeadLetterSchema.Columns.Status}]
            FROM {fullTableName}
            WHERE [{CommandDeadLetterSchema.Columns.Status}] = {(short)CommandDeadLetterStatus.New}
            ORDER BY [{CommandDeadLetterSchema.Columns.OccurredAt}] ASC
            """;

        _getByIdSql = $"""
            SELECT [{CommandDeadLetterSchema.Columns.Id}],
                   [{CommandDeadLetterSchema.Columns.CommandType}],
                   [{CommandDeadLetterSchema.Columns.Payload}],
                   [{CommandDeadLetterSchema.Columns.ExceptionType}],
                   [{CommandDeadLetterSchema.Columns.ExceptionMessage}],
                   [{CommandDeadLetterSchema.Columns.OccurredAt}],
                   [{CommandDeadLetterSchema.Columns.AttemptCount}],
                   [{CommandDeadLetterSchema.Columns.Status}]
            FROM {fullTableName}
            WHERE [{CommandDeadLetterSchema.Columns.Id}] = @Id
            """;

        _updateStatusSql = $"""
            UPDATE {fullTableName}
            SET [{CommandDeadLetterSchema.Columns.Status}] = @Status
            WHERE [{CommandDeadLetterSchema.Columns.Id}] = @Id
            """;

        _getStatisticsSql = $"""
            SELECT [{CommandDeadLetterSchema.Columns.Status}], COUNT(*) AS [Count]
            FROM {fullTableName}
            GROUP BY [{CommandDeadLetterSchema.Columns.Status}]
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
            var command = new SqlCommand(_getPendingSql, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@count", count);

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
                await GetEntryByIdAsync(connection, id, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"CommandDeadLetterEntry '{id}' was not found.");

            _ = await UpdateStatusAsync(connection, id, CommandDeadLetterStatus.Replaying, cancellationToken)
                .ConfigureAwait(false);

            await CommandDeadLetterReplayDispatcher
                .ReplayAsync(_mediator, _payloadSerializer, entry.CommandType, entry.Payload, cancellationToken)
                .ConfigureAwait(false);

            _ = await UpdateStatusAsync(connection, id, CommandDeadLetterStatus.Resolved, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DismissAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var affected = await UpdateStatusAsync(connection, id, CommandDeadLetterStatus.Dismissed, cancellationToken)
                .ConfigureAwait(false);

            if (affected == 0)
            {
                throw new KeyNotFoundException($"CommandDeadLetterEntry '{id}' was not found.");
            }
        }
    }

    /// <inheritdoc />
    public async Task<CommandDeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new SqlCommand(_getStatisticsSql, connection);
            await using (command.ConfigureAwait(false))
            {
                var newCount = 0;
                var replayingCount = 0;
                var resolvedCount = 0;
                var dismissedCount = 0;

                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    var ordStatus = reader.GetOrdinal(CommandDeadLetterSchema.Columns.Status);
                    var ordCount = reader.GetOrdinal("Count");

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var status = (CommandDeadLetterStatus)reader.GetInt16(ordStatus);
                        var count = reader.GetInt32(ordCount);

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

                return new CommandDeadLetterStatistics(newCount, replayingCount, resolvedCount, dismissedCount);
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
    /// Retrieves the dead letter entry identified by <paramref name="id"/>, or <see langword="null"/>
    /// when no matching entry exists.
    /// </summary>
    /// <param name="connection">The open connection to use for the query.</param>
    /// <param name="id">The identifier of the dead letter entry to retrieve.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The matching <see cref="CommandDeadLetterEntry"/>, or <see langword="null"/>.</returns>
    private async Task<CommandDeadLetterEntry?> GetEntryByIdAsync(
        SqlConnection connection,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var command = new SqlCommand(_getByIdSql, connection);
        await using (command.ConfigureAwait(false))
        {
            _ = command.Parameters.AddWithValue("@Id", id);

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
    /// <param name="connection">The open connection to use for the update.</param>
    /// <param name="id">The identifier of the dead letter entry to update.</param>
    /// <param name="status">The new status to apply.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The number of rows affected by the update.</returns>
    private async Task<int> UpdateStatusAsync(
        SqlConnection connection,
        Guid id,
        CommandDeadLetterStatus status,
        CancellationToken cancellationToken
    )
    {
        var command = new SqlCommand(_updateStatusSql, connection);
        await using (command.ConfigureAwait(false))
        {
            _ = command.Parameters.AddWithValue("@Id", id);
            _ = command.Parameters.AddWithValue("@Status", (short)status);

            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Maps the current row of a <see cref="SqlDataReader"/> to a new <see cref="CommandDeadLetterEntry"/> instance.
    /// </summary>
    /// <param name="reader">The reader positioned on the row to map.</param>
    /// <returns>A populated <see cref="CommandDeadLetterEntry"/>.</returns>
    private static CommandDeadLetterEntry MapToEntry(SqlDataReader reader)
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
            OccurredAt = reader.GetDateTimeOffset(ordOccurredAt),
            AttemptCount = reader.GetInt32(ordAttemptCount),
            Status = (CommandDeadLetterStatus)reader.GetInt16(ordStatus),
        };
    }
}
