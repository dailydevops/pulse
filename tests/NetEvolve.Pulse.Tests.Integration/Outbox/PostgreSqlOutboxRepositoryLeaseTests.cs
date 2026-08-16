namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;
using Npgsql;
using TUnit.Core;

[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "The setup script is read from a checked-in .sql file with schema/table names substituted from test-generated GUID values, not external user input."
)]
[TestGroup("PostgreSql")]
public sealed partial class PostgreSqlOutboxRepositoryLeaseTests
{
    [ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerTestSession)]
    public required PostgreSqlContainerFixture Container { get; init; }

    private static readonly string _scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "Scripts",
        "PostgreSql",
        "OutboxMessage.sql"
    );

    private static OutboxMessage CreateMessage(DateTimeOffset createdAt) =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(LeaseTestEvent),
            Payload = "{}",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            Status = OutboxMessageStatus.Pending,
        };

    private sealed record LeaseTestEvent;

    private async Task<(PostgreSqlOutboxRepository Repository, OutboxOptions Options)> CreateRepositoryAsync(
        TimeSpan processingLeaseTimeout,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var databaseName = $"lease{Guid.NewGuid():N}";
        var schema = $"lease{Guid.NewGuid():N}";

        await using (var adminConnection = new NpgsqlConnection(Container.ConnectionString))
        {
            await adminConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2100 // DatabaseName is test-controlled, not user input
            var createDbCommand = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", adminConnection);
#pragma warning restore CA2100
            await using (createDbCommand.ConfigureAwait(false))
            {
                _ = await createDbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var builder = new NpgsqlConnectionStringBuilder(Container.ConnectionString) { Database = databaseName };
        var connectionString = builder.ToString();

        var script = await File.ReadAllTextAsync(_scriptPath, cancellationToken).ConfigureAwait(false);
        script = SearchSetVar().Replace(script, string.Empty);
        script = script
            .Replace(":schema_name", schema, StringComparison.Ordinal)
            .Replace(":table_name", "OutboxMessage", StringComparison.Ordinal);

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new NpgsqlCommand(script, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var options = new OutboxOptions
        {
            ConnectionString = connectionString,
            Schema = schema,
            TableName = "OutboxMessage",
            ProcessingLeaseTimeout = processingLeaseTimeout,
        };

        var repository = new PostgreSqlOutboxRepository(Options.Create(options), TimeProvider.System);

        return (repository, options);
    }

    private static async Task SetUpdatedAtInThePastAsync(
        OutboxOptions options,
        Guid messageId,
        TimeSpan age,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2100 // Schema/TableName are test-controlled, not user input
        var command = new NpgsqlCommand(
            $"""
            UPDATE "{options.Schema}"."{options.TableName}"
            SET "UpdatedAt" = NOW() - INTERVAL '1 second' * @age_seconds
            WHERE "Id" = @id
            """,
            connection
        );
#pragma warning restore CA2100
        await using (command.ConfigureAwait(false))
        {
            _ = command.Parameters.AddWithValue("age_seconds", age.TotalSeconds);
            _ = command.Parameters.AddWithValue("id", messageId);
            _ = await command.ExecuteNonQueryAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GetPendingAsync_WhenProcessingLeaseExpired_ReclaimsClaimedMessage(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (repository, options) = await CreateRepositoryAsync(TimeSpan.FromMinutes(5), cancellationToken)
            .ConfigureAwait(false);

        var message = CreateMessage(DateTimeOffset.UtcNow);
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(claimed).Count().IsEqualTo(1);

        // Simulate a crashed worker: the message stays claimed (Processing) far beyond the lease.
        await SetUpdatedAtInThePastAsync(options, message.Id, TimeSpan.FromMinutes(10), cancellationToken)
            .ConfigureAwait(false);

        var reclaimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(reclaimed).Count().IsEqualTo(1);
        _ = await Assert.That(reclaimed[0].Id).IsEqualTo(message.Id);
    }

    [Test]
    public async Task GetPendingAsync_WhileProcessingLeaseActive_DoesNotReclaimClaimedMessage(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (repository, options) = await CreateRepositoryAsync(TimeSpan.FromMinutes(5), cancellationToken)
            .ConfigureAwait(false);

        var message = CreateMessage(DateTimeOffset.UtcNow);
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(claimed).Count().IsEqualTo(1);

        // Only a minute has passed - well within the 5-minute lease.
        await SetUpdatedAtInThePastAsync(options, message.Id, TimeSpan.FromMinutes(1), cancellationToken)
            .ConfigureAwait(false);

        var reclaimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(reclaimed).IsEmpty();
    }

    [Test]
    public async Task ScriptRedeploy_OverPreExistingSingleArgumentFunction_LeavesCallableUnambiguousFunction(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var databaseName = $"lease{Guid.NewGuid():N}";
        var schema = $"lease{Guid.NewGuid():N}";

        await using (var adminConnection = new NpgsqlConnection(Container.ConnectionString))
        {
            await adminConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var createDbCommand = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", adminConnection);
            await using (createDbCommand.ConfigureAwait(false))
            {
                _ = await createDbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var builder = new NpgsqlConnectionStringBuilder(Container.ConnectionString) { Database = databaseName };
        var connectionString = builder.ToString();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Simulate a database provisioned by a version of this script that predates the lease-reclaim fix:
            // the table exists and get_pending_outbox_messages has only the single-parameter signature.
            var setupCommand = new NpgsqlCommand(
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
                CREATE FUNCTION "{schema}".get_pending_outbox_messages(
                    batch_size INTEGER
                )
                RETURNS TABLE ("Id" UUID)
                LANGUAGE plpgsql
                AS $body$
                BEGIN
                    RETURN QUERY
                    SELECT om."Id"
                    FROM "{schema}"."OutboxMessage" om
                    WHERE om."Status" = 0
                    LIMIT batch_size;
                END;
                $body$;
                """,
                connection
            );
            await using (setupCommand.ConfigureAwait(false))
            {
                _ = await setupCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // Redeploy the current (fixed) script over the pre-existing installation.
        var script = await File.ReadAllTextAsync(_scriptPath, cancellationToken).ConfigureAwait(false);
        script = SearchSetVar().Replace(script, string.Empty);
        script = script
            .Replace(":schema_name", schema, StringComparison.Ordinal)
            .Replace(":table_name", "OutboxMessage", StringComparison.Ordinal);

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new NpgsqlCommand(script, connection);
            await using (command.ConfigureAwait(false))
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // A one-argument call must resolve unambiguously (via the new parameter's default),
        // instead of failing with "function ... is not unique" because two overloads exist.
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var callCommand = new NpgsqlCommand(
                $"SELECT * FROM \"{schema}\".get_pending_outbox_messages(10)",
                connection
            );
            await using (callCommand.ConfigureAwait(false))
            {
                await using var reader = await callCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                // No rows expected (table is empty); reaching this line without an exception is the assertion.
            }
        }
    }

    [GeneratedRegex(@"^\\set\s+\w+\s+.*$", RegexOptions.Multiline, 10000)]
    private static partial Regex SearchSetVar();
}
