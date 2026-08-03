namespace NetEvolve.Pulse.Tests.Integration.Internals.DeadLetter;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;

[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is constructed from validated CommandDeadLetterOptions.TableName property, not user input."
)]
public sealed class SQLiteAdoNetCommandDeadLetterInitializer : IServiceInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(mediatorBuilder);
        ArgumentNullException.ThrowIfNull(serviceFixture);
        _ = mediatorBuilder.AddSQLiteCommandDeadLetterStore(options =>
            options.ConnectionString = serviceFixture.ConnectionString
        );
    }

    public async ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var options = serviceProvider.GetRequiredService<IOptions<CommandDeadLetterOptions>>().Value;

        var connectionString =
            options.ConnectionString
            ?? throw new InvalidOperationException("CommandDeadLetterOptions.ConnectionString is not configured.");

        var tableName = string.IsNullOrWhiteSpace(options.TableName)
            ? CommandDeadLetterSchema.DefaultTableName
            : options.TableName;

        var quotedTable = $"\"{tableName}\"";
        var quotedPk = $"\"PK_{tableName}\"";
        var quotedIdx = $"\"IX_{tableName}_Status\"";

        var createTableSql = $"""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS {quotedTable} (
                "{CommandDeadLetterSchema.Columns.Id}"               TEXT NOT NULL,
                "{CommandDeadLetterSchema.Columns.CommandType}"      TEXT NOT NULL,
                "{CommandDeadLetterSchema.Columns.Payload}"          TEXT NOT NULL,
                "{CommandDeadLetterSchema.Columns.ExceptionType}"    TEXT NULL,
                "{CommandDeadLetterSchema.Columns.ExceptionMessage}" TEXT NULL,
                "{CommandDeadLetterSchema.Columns.OccurredAt}"       TEXT NOT NULL,
                "{CommandDeadLetterSchema.Columns.AttemptCount}"     INTEGER NOT NULL,
                "{CommandDeadLetterSchema.Columns.Status}"           INTEGER NOT NULL,
                CONSTRAINT {quotedPk} PRIMARY KEY ("{CommandDeadLetterSchema.Columns.Id}")
            );
            CREATE INDEX IF NOT EXISTS {quotedIdx}
                ON {quotedTable} ("{CommandDeadLetterSchema.Columns.Status}");
            """;

        var connection = new SqliteConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

#pragma warning disable S2077 // SQL is constructed from validated CommandDeadLetterOptions.TableName property, not user input.
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
