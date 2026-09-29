namespace NetEvolve.Pulse.Tests.Integration.Schema;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;
using TUnit.Core;

[TestGroup("MySql")]
[Timeout(300_000)] // MySQL Testcontainer cold-start can take a while in CI environments.
public sealed class MySqlSchemaScriptTests
{
    [ClassDataSource<MySqlContainerFixture>(Shared = SharedType.PerTestSession)]
    public required MySqlContainerFixture Container { get; init; }

    [Test]
    [Arguments(
        "OutboxMessage.sql",
        "OutboxMessage",
        new[]
        {
            "IX_OutboxMessage_Status_CreatedAt",
            "IX_OutboxMessage_Status_NextRetryAt",
            "IX_OutboxMessage_Status_ProcessedAt",
            "IX_OutboxMessage_Status_UpdatedAt",
        }
    )]
    [Arguments("IdempotencyKey.sql", "IdempotencyKey", new[] { "IX_IdempotencyKey_CreatedAt" })]
    [Arguments("AuditEntry.sql", "AuditEntry", new[] { "IX_AuditEntry_OccurredAt", "IX_AuditEntry_CommandType" })]
    [Arguments(
        "CommandDeadLetter.sql",
        "CommandDeadLetter",
        new[] { "IX_CommandDeadLetter_Status", "IX_CommandDeadLetter_OccurredAt" }
    )]
    public async Task Script_WhenRunTwice_SucceedsAndKeepsAllIndexes(
        string scriptName,
        string tableName,
        string[] expectedIndexes,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionString = await CreateDatabaseAsync(cancellationToken).ConfigureAwait(false);

        await MySqlScriptRunner
            .ExecuteAsync(connectionString, scriptName, tableName, tableName, cancellationToken)
            .ConfigureAwait(false);
        await MySqlScriptRunner
            .ExecuteAsync(connectionString, scriptName, tableName, tableName, cancellationToken)
            .ConfigureAwait(false);

        var indexes = await GetIndexNamesAsync(connectionString, tableName, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(indexes).IsEquivalentTo(expectedIndexes);
    }

    [Test]
    public async Task OutboxMessageScript_WhenLeaseIndexMissing_CreatesItAndKeepsExistingIndexes(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionString = await CreateDatabaseAsync(cancellationToken).ConfigureAwait(false);

        await MySqlScriptRunner
            .ExecuteAsync(connectionString, "OutboxMessage.sql", "OutboxMessage", "OutboxMessage", cancellationToken)
            .ConfigureAwait(false);

        // Simulate a deployment created before IX_OutboxMessage_Status_UpdatedAt was introduced.
        await ExecuteAsync(
                connectionString,
                "DROP INDEX `IX_OutboxMessage_Status_UpdatedAt` ON `OutboxMessage`",
                cancellationToken
            )
            .ConfigureAwait(false);

        await MySqlScriptRunner
            .ExecuteAsync(connectionString, "OutboxMessage.sql", "OutboxMessage", "OutboxMessage", cancellationToken)
            .ConfigureAwait(false);

        var indexes = await GetIndexNamesAsync(connectionString, "OutboxMessage", cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert
            .That(indexes)
            .IsEquivalentTo([
                "IX_OutboxMessage_Status_CreatedAt",
                "IX_OutboxMessage_Status_NextRetryAt",
                "IX_OutboxMessage_Status_ProcessedAt",
                "IX_OutboxMessage_Status_UpdatedAt",
            ]);
    }

    [Test]
    public async Task IdempotencyKeyScript_WhenKeyColumnIsCaseInsensitive_SwitchesToBinaryCollation(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionString = await CreateDatabaseAsync(cancellationToken).ConfigureAwait(false);

        // Simulate a deployment created before the key column got a binary collation.
        await ExecuteAsync(
                connectionString,
                """
                CREATE TABLE `IdempotencyKey` (
                    `IdempotencyKey` VARCHAR(500) NOT NULL,
                    `CreatedAt`      BIGINT       NOT NULL,
                    CONSTRAINT `PK_IdempotencyKey` PRIMARY KEY (`IdempotencyKey`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
                """,
                cancellationToken
            )
            .ConfigureAwait(false);
        await ExecuteAsync(
                connectionString,
                "INSERT INTO `IdempotencyKey` (`IdempotencyKey`, `CreatedAt`) VALUES ('aBc123', 0)",
                cancellationToken
            )
            .ConfigureAwait(false);

        await MySqlScriptRunner
            .ExecuteAsync(connectionString, "IdempotencyKey.sql", "IdempotencyKey", "IdempotencyKey", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
                connectionString,
                "INSERT INTO `IdempotencyKey` (`IdempotencyKey`, `CreatedAt`) VALUES ('ABC123', 0)",
                cancellationToken
            )
            .ConfigureAwait(false);

        var collation = await GetKeyColumnCollationAsync(connectionString, "IdempotencyKey", cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(collation).IsEqualTo("utf8mb4_bin");
    }

    private static async Task<string?> GetKeyColumnCollationAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new MySqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new MySqlCommand(
                """
                SELECT COLLATION_NAME
                FROM information_schema.columns
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName AND COLUMN_NAME = 'IdempotencyKey'
                """,
                connection
            );
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@tableName", tableName);

                return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            }
        }
    }

    private async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var databaseName = $"pulse{Guid.NewGuid():N}";

#pragma warning disable CA2100, S2077 // databaseName is test-controlled, not user input
        await ExecuteAsync(Container.ConnectionString, $"CREATE DATABASE `{databaseName}`", cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA2100, S2077

        return Container.ConnectionString.Replace(
            ";Database=test;",
            $";Database={databaseName};",
            StringComparison.Ordinal
        );
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new MySqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2100, S2077 // sql is a test-controlled constant, not user input
            var command = new MySqlCommand(sql, connection);
#pragma warning restore CA2100, S2077
            await using (command.ConfigureAwait(false))
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<List<string>> GetIndexNamesAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var indexes = new List<string>();

        var connection = new MySqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new MySqlCommand(
                """
                SELECT DISTINCT INDEX_NAME
                FROM information_schema.statistics
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName AND INDEX_NAME <> 'PRIMARY'
                """,
                connection
            );
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@tableName", tableName);

                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        indexes.Add(reader.GetString(0));
                    }
                }
            }
        }

        return indexes;
    }
}
