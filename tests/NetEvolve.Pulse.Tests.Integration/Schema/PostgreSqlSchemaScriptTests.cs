namespace NetEvolve.Pulse.Tests.Integration.Schema;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;
using Npgsql;
using TUnit.Core;

[TestGroup("PostgreSql")]
[Timeout(300_000)] // PostgreSQL Testcontainer cold-start can take a while in CI environments.
public sealed class PostgreSqlSchemaScriptTests
{
    [ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerTestSession)]
    public required PostgreSqlContainerFixture Container { get; init; }

    [Test]
    [Arguments("OutboxMessage.sql", new[] { "_Status_CreatedAt", "_Status_ProcessedAt" }, 15)]
    [Arguments("IdempotencyKey.sql", new[] { "_created_at" }, 4)]
    [Arguments("AuditEntry.sql", new[] { "_OccurredAt", "_CommandType" }, 0)]
    [Arguments("CommandDeadLetter.sql", new[] { "_Status", "_OccurredAt" }, 0)]
    public async Task Script_WhenRunTwiceThroughPsql_CreatesAllObjectsUnderConfiguredNames(
        string scriptName,
        string[] indexSuffixes,
        int expectedFunctionCount,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (databaseName, connectionString) = await CreateDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var schema = CreateMixedCaseSchemaName();
        const string tableName = "Custom_Table";

        await Container
            .RunScriptAsync(scriptName, databaseName, schema, tableName, cancellationToken)
            .ConfigureAwait(false);
        await Container
            .RunScriptAsync(scriptName, databaseName, schema, tableName, cancellationToken)
            .ConfigureAwait(false);

        var indexes = await GetIndexNamesAsync(connectionString, schema, tableName, cancellationToken)
            .ConfigureAwait(false);
        var functionBodies = await GetFunctionBodiesAsync(connectionString, schema, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert
            .That(indexes)
            .IsEquivalentTo([
                $"PK_{schema}_{tableName}",
                .. indexSuffixes.Select(suffix => $"IX_{schema}_{tableName}{suffix}"),
            ]);
        _ = await Assert.That(functionBodies).Count().IsEqualTo(expectedFunctionCount);
        _ = await Assert
            .That(
                functionBodies.Where(body => !body.Contains($"\"{schema}\".\"{tableName}\"", StringComparison.Ordinal))
            )
            .IsEmpty();
    }

    [Test]
    public async Task OutboxMessageScript_WithTwoTableNamesInSameSchema_CreatesSeparateKeysAndIndexes(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (databaseName, connectionString) = await CreateDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var schema = CreateMixedCaseSchemaName();

        await Container
            .RunScriptAsync("OutboxMessage.sql", databaseName, schema, "OutboxA", cancellationToken)
            .ConfigureAwait(false);
        await Container
            .RunScriptAsync("OutboxMessage.sql", databaseName, schema, "OutboxB", cancellationToken)
            .ConfigureAwait(false);

        var indexesA = await GetIndexNamesAsync(connectionString, schema, "OutboxA", cancellationToken)
            .ConfigureAwait(false);
        var indexesB = await GetIndexNamesAsync(connectionString, schema, "OutboxB", cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert
            .That(indexesA)
            .IsEquivalentTo([
                $"PK_{schema}_OutboxA",
                $"IX_{schema}_OutboxA_Status_CreatedAt",
                $"IX_{schema}_OutboxA_Status_ProcessedAt",
            ]);
        _ = await Assert
            .That(indexesB)
            .IsEquivalentTo([
                $"PK_{schema}_OutboxB",
                $"IX_{schema}_OutboxB_Status_CreatedAt",
                $"IX_{schema}_OutboxB_Status_ProcessedAt",
            ]);
    }

    [Test]
    public async Task OutboxMessageScript_OverLegacyIndexNames_RenamesThemInsteadOfAddingDuplicates(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (databaseName, connectionString) = await CreateDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var schema = CreateMixedCaseSchemaName();

        // Simulate a deployment made from the earlier, hand-substituted script whose key and index
        // names did not include the table name.
        await ExecuteAsync(
                connectionString,
                $"""
                CREATE SCHEMA "{schema}";
                CREATE TABLE "{schema}"."OutboxMessage" (
                    "Id"            UUID            NOT NULL,
                    "EventType"     VARCHAR(500)    NOT NULL,
                    "Payload"       TEXT            NOT NULL,
                    "CorrelationId" VARCHAR(100)    NULL,
                    "CausationId"   VARCHAR(100)    NULL,
                    "CreatedAt"     TIMESTAMPTZ     NOT NULL,
                    "UpdatedAt"     TIMESTAMPTZ     NOT NULL,
                    "ProcessedAt"   TIMESTAMPTZ     NULL,
                    "NextRetryAt"   TIMESTAMPTZ     NULL,
                    "RetryCount"    INTEGER         NOT NULL DEFAULT 0,
                    "Error"         TEXT            NULL,
                    "Status"        INTEGER         NOT NULL DEFAULT 0,
                    CONSTRAINT "PK_{schema}" PRIMARY KEY ("Id")
                );
                CREATE INDEX "IX_{schema}_Status_CreatedAt" ON "{schema}"."OutboxMessage" ("Status", "CreatedAt")
                WHERE "Status" IN (0, 3);
                CREATE INDEX "IX_{schema}_Status_ProcessedAt" ON "{schema}"."OutboxMessage" ("Status", "ProcessedAt")
                WHERE "Status" = 2;
                """,
                cancellationToken
            )
            .ConfigureAwait(false);

        await Container
            .RunScriptAsync("OutboxMessage.sql", databaseName, schema, "OutboxMessage", cancellationToken)
            .ConfigureAwait(false);

        var indexes = await GetIndexNamesAsync(connectionString, schema, "OutboxMessage", cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert
            .That(indexes)
            .IsEquivalentTo([
                $"PK_{schema}_OutboxMessage",
                $"IX_{schema}_OutboxMessage_Status_CreatedAt",
                $"IX_{schema}_OutboxMessage_Status_ProcessedAt",
            ]);
    }

    private static string CreateMixedCaseSchemaName() => $"Pulse_{Guid.NewGuid().ToString("N")[..8]}";

    private async Task<(string DatabaseName, string ConnectionString)> CreateDatabaseAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var databaseName = $"schema{Guid.NewGuid():N}";

#pragma warning disable CA2100, S2077 // databaseName is test-controlled, not user input
        await ExecuteAsync(Container.ConnectionString, $"CREATE DATABASE \"{databaseName}\"", cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA2100, S2077

        var builder = new NpgsqlConnectionStringBuilder(Container.ConnectionString) { Database = databaseName };
        return (databaseName, builder.ToString());
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2100, S2077 // sql is built from test-controlled values, not user input
            var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100, S2077
            await using (command.ConfigureAwait(false))
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task<List<string>> GetIndexNamesAsync(
        string connectionString,
        string schema,
        string tableName,
        CancellationToken cancellationToken
    ) =>
        QueryStringsAsync(
            connectionString,
            "SELECT indexname FROM pg_indexes WHERE schemaname = @schema AND tablename = @table",
            schema,
            tableName,
            cancellationToken
        );

    private static Task<List<string>> GetFunctionBodiesAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken
    ) =>
        QueryStringsAsync(
            connectionString,
            """
            SELECT p.prosrc
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = @schema
            """,
            schema,
            tableName: null,
            cancellationToken
        );

    private static async Task<List<string>> QueryStringsAsync(
        string connectionString,
        string sql,
        string schema,
        string? tableName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var values = new List<string>();

        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2100, S2077 // sql is a constant query of this class; values are passed as parameters
            var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100, S2077
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("schema", schema);
                if (tableName is not null)
                {
                    _ = command.Parameters.AddWithValue("table", tableName);
                }

                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        values.Add(reader.GetString(0));
                    }
                }
            }
        }

        return values;
    }
}
