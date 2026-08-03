namespace NetEvolve.Pulse.Tests.Integration.Internals.Audit;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is constructed from validated AuditStoreOptions.TableName property, not user input."
)]
public sealed class SQLiteAdoNetAuditInitializer : IServiceInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(mediatorBuilder);
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _ = mediatorBuilder.AddSQLiteAuditStore(options => options.ConnectionString = serviceFixture.ConnectionString);
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var options = serviceProvider.GetRequiredService<IOptions<AuditStoreOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("AuditStoreOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? AuditEntrySchema.DefaultTableName
            : options.TableName;

        var quotedTable = $"\"{tableName}\"";
        var quotedPk = $"\"PK_{tableName}\"";
        var quotedIdxOccurredAt = $"\"IX_{tableName}_OccurredAt\"";
        var quotedIdxCommandType = $"\"IX_{tableName}_CommandType\"";

        var createTableSql = $"""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS {quotedTable} (
                "{AuditEntrySchema.Columns.Id}"               TEXT NOT NULL,
                "{AuditEntrySchema.Columns.CommandType}"      TEXT NOT NULL,
                "{AuditEntrySchema.Columns.UserId}"           TEXT NULL,
                "{AuditEntrySchema.Columns.CorrelationId}"    TEXT NULL,
                "{AuditEntrySchema.Columns.OccurredAt}"       TEXT NOT NULL,
                "{AuditEntrySchema.Columns.DurationMs}"       REAL NOT NULL,
                "{AuditEntrySchema.Columns.Result}"           INTEGER NOT NULL,
                "{AuditEntrySchema.Columns.Payload}"          TEXT NULL,
                "{AuditEntrySchema.Columns.ExceptionMessage}" TEXT NULL,
                CONSTRAINT {quotedPk} PRIMARY KEY ("{AuditEntrySchema.Columns.Id}")
            );
            CREATE INDEX IF NOT EXISTS {quotedIdxOccurredAt}
                ON {quotedTable} ("{AuditEntrySchema.Columns.OccurredAt}");
            CREATE INDEX IF NOT EXISTS {quotedIdxCommandType}
                ON {quotedTable} ("{AuditEntrySchema.Columns.CommandType}");
            """;

        var connection = new SqliteConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

#pragma warning disable S2077 // SQL is constructed from validated AuditStoreOptions.TableName property, not user input.
            var command = new SqliteCommand(createTableSql, connection);
#pragma warning restore S2077
            await using (command.ConfigureAwait(false))
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture) { }
}
