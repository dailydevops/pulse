namespace NetEvolve.Pulse.Audit;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// MySQL implementation of <see cref="IAuditManagement"/> using ADO.NET.
/// Provides filtered querying and statistics aggregation for the audit trail store.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// Execute the schema script from <c>Scripts/AuditEntry.sql</c> to create the required
/// database objects before using this provider.
/// <para><strong>Schema:</strong></para>
/// MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
/// All tables reside in the active database specified by the connection string.
/// The <see cref="AuditStoreOptions.Schema"/> property is ignored for MySQL.
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
    Justification = "SQL is constructed from validated AuditStoreOptions.TableName property, not user input."
)]
internal sealed class MySqlAuditManagement : IAuditManagement
{
    private readonly string _connectionString;
    private readonly string _table;
    private readonly string _getStatisticsSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlAuditManagement"/> class.
    /// </summary>
    /// <param name="options">The audit trail store configuration options.</param>
    public MySqlAuditManagement(IOptions<AuditStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ConnectionString);

        var opts = options.Value;
        _connectionString = opts.ConnectionString;

        SqlIdentifier.Validate(opts.TableName, nameof(opts.TableName));
        _table = $"`{opts.TableName}`";

        _getStatisticsSql = $"""
            SELECT `{AuditEntrySchema.Columns.Result}`, COUNT(*)
            FROM {_table}
            GROUP BY `{AuditEntrySchema.Columns.Result}`
            """;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditRecord>> QueryAsync(
        AuditFilter filter,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(filter);

        var sql = new StringBuilder();
        _ = sql.Append(
            CultureInfo.InvariantCulture,
            $"""
            SELECT
                `{AuditEntrySchema.Columns.Id}`,
                `{AuditEntrySchema.Columns.CommandType}`,
                `{AuditEntrySchema.Columns.UserId}`,
                `{AuditEntrySchema.Columns.CorrelationId}`,
                `{AuditEntrySchema.Columns.OccurredAt}`,
                `{AuditEntrySchema.Columns.DurationMs}`,
                `{AuditEntrySchema.Columns.Result}`,
                `{AuditEntrySchema.Columns.Payload}`,
                `{AuditEntrySchema.Columns.ExceptionMessage}`
            FROM {_table}
            WHERE 1 = 1
            """
        );

        if (filter.CommandType is not null)
        {
            _ = sql.Append(
                CultureInfo.InvariantCulture,
                $" AND `{AuditEntrySchema.Columns.CommandType}` = @commandType"
            );
        }

        if (filter.UserId is not null)
        {
            _ = sql.Append(CultureInfo.InvariantCulture, $" AND `{AuditEntrySchema.Columns.UserId}` = @userId");
        }

        if (filter.From is not null)
        {
            _ = sql.Append(CultureInfo.InvariantCulture, $" AND `{AuditEntrySchema.Columns.OccurredAt}` >= @from");
        }

        if (filter.To is not null)
        {
            _ = sql.Append(CultureInfo.InvariantCulture, $" AND `{AuditEntrySchema.Columns.OccurredAt}` <= @to");
        }

        if (filter.Result is not null)
        {
            _ = sql.Append(CultureInfo.InvariantCulture, $" AND `{AuditEntrySchema.Columns.Result}` = @result");
        }

        _ = sql.Append(CultureInfo.InvariantCulture, $" ORDER BY `{AuditEntrySchema.Columns.OccurredAt}` DESC");
        _ = sql.Append(" LIMIT @take OFFSET @skip");

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new MySqlCommand(sql.ToString(), connection);
            await using (command.ConfigureAwait(false))
            {
                if (filter.CommandType is not null)
                {
                    _ = command.Parameters.AddWithValue("@commandType", filter.CommandType);
                }

                if (filter.UserId is not null)
                {
                    _ = command.Parameters.AddWithValue("@userId", filter.UserId);
                }

                if (filter.From is not null)
                {
                    _ = command.Parameters.AddWithValue("@from", filter.From.Value.UtcTicks);
                }

                if (filter.To is not null)
                {
                    _ = command.Parameters.AddWithValue("@to", filter.To.Value.UtcTicks);
                }

                if (filter.Result is not null)
                {
                    _ = command.Parameters.AddWithValue("@result", (int)filter.Result.Value);
                }

                _ = command.Parameters.AddWithValue("@take", filter.Take);
                _ = command.Parameters.AddWithValue("@skip", filter.Skip);

                return await ReadRecordsAsync(command, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<AuditStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var successCount = 0;
        var failureCount = 0;

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
                        var result = (AuditResult)reader.GetInt32(0);
                        var count = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);

                        switch (result)
                        {
                            case AuditResult.Success:
                                successCount = count;
                                break;
                            case AuditResult.Failure:
                                failureCount = count;
                                break;
                        }
                    }
                }
            }
        }

        return new AuditStatistics(successCount, failureCount);
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
    /// Executes <paramref name="command"/> and reads all rows into a list of <see cref="AuditRecord"/> instances.
    /// </summary>
    private static async Task<IReadOnlyList<AuditRecord>> ReadRecordsAsync(
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

            var ordId = reader.GetOrdinal(AuditEntrySchema.Columns.Id);
            var ordCommandType = reader.GetOrdinal(AuditEntrySchema.Columns.CommandType);
            var ordUserId = reader.GetOrdinal(AuditEntrySchema.Columns.UserId);
            var ordCorrelationId = reader.GetOrdinal(AuditEntrySchema.Columns.CorrelationId);
            var ordOccurredAt = reader.GetOrdinal(AuditEntrySchema.Columns.OccurredAt);
            var ordDurationMs = reader.GetOrdinal(AuditEntrySchema.Columns.DurationMs);
            var ordResult = reader.GetOrdinal(AuditEntrySchema.Columns.Result);
            var ordPayload = reader.GetOrdinal(AuditEntrySchema.Columns.Payload);
            var ordExceptionMessage = reader.GetOrdinal(AuditEntrySchema.Columns.ExceptionMessage);

            var records = new List<AuditRecord>();
            do
            {
                var idBytes = await reader.GetFieldValueAsync<byte[]>(ordId, cancellationToken).ConfigureAwait(false);
                var userIdNull = await reader.IsDBNullAsync(ordUserId, cancellationToken).ConfigureAwait(false);
                var correlationIdNull = await reader
                    .IsDBNullAsync(ordCorrelationId, cancellationToken)
                    .ConfigureAwait(false);
                var payloadNull = await reader.IsDBNullAsync(ordPayload, cancellationToken).ConfigureAwait(false);
                var exceptionMessageNull = await reader
                    .IsDBNullAsync(ordExceptionMessage, cancellationToken)
                    .ConfigureAwait(false);

                records.Add(
                    new AuditRecord
                    {
                        Id = new Guid(idBytes),
                        CommandType = reader.GetString(ordCommandType),
                        UserId = userIdNull ? null : reader.GetString(ordUserId),
                        CorrelationId = correlationIdNull ? null : reader.GetString(ordCorrelationId),
                        OccurredAt = new DateTimeOffset(reader.GetInt64(ordOccurredAt), TimeSpan.Zero),
                        DurationMs = reader.GetDouble(ordDurationMs),
                        Result = (AuditResult)reader.GetInt32(ordResult),
                        Payload = payloadNull ? null : reader.GetString(ordPayload),
                        ExceptionMessage = exceptionMessageNull ? null : reader.GetString(ordExceptionMessage),
                    }
                );
            } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));

            return records;
        }
    }
}
