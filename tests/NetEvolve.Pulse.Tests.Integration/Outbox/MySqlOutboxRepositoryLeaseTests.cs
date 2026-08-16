namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MySql.Data.MySqlClient;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;
using TUnit.Core;

[TestGroup("MySql")]
[Timeout(300_000)] // MySQL Testcontainer cold-start can take a while in CI environments.
public sealed class MySqlOutboxRepositoryLeaseTests
{
    [ClassDataSource<MySqlContainerFixture>(Shared = SharedType.PerTestSession)]
    public required MySqlContainerFixture Container { get; init; }

    private sealed record LeaseTestEvent;

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

    private async Task<(MySqlOutboxRepository Repository, FakeTimeProvider TimeProvider)> CreateRepositoryAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var databaseName = $"pulse{Guid.NewGuid():N}";
        var tableName = $"OutboxMessage_{Guid.NewGuid():N}";

        var setupConnection = new MySqlConnection(Container.ConnectionString);
        await using (setupConnection.ConfigureAwait(false))
        {
            await setupConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var createDatabaseCommand = setupConnection.CreateCommand();
            await using (createDatabaseCommand.ConfigureAwait(false))
            {
#pragma warning disable CA2100, S2077 // databaseName is test-controlled, not user input
                createDatabaseCommand.CommandText = $"CREATE DATABASE `{databaseName}`";
#pragma warning restore CA2100, S2077
                _ = await createDatabaseCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var connectionString = Container.ConnectionString.Replace(
            ";Database=test;",
            $";Database={databaseName};",
            StringComparison.Ordinal
        );

        await CreateSchemaAsync(connectionString, tableName, cancellationToken).ConfigureAwait(false);

        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(
            new OutboxOptions
            {
                ConnectionString = connectionString,
                TableName = tableName,
                ProcessingLeaseTimeout = TimeSpan.FromMinutes(5),
            }
        );

        var repository = new MySqlOutboxRepository(options, timeProvider);
        return (repository, timeProvider);
    }

    private static async Task CreateSchemaAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scriptPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Scripts", "MySql", "OutboxMessage.sql");
        var script = await System.IO.File.ReadAllTextAsync(scriptPath, cancellationToken).ConfigureAwait(false);

        script = script.Replace("`OutboxMessage`", $"`{tableName}`", StringComparison.Ordinal);

        var connection = new MySqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            foreach (
                var statement in script.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                if (IsCommentOrEmpty(statement))
                {
                    continue;
                }

#pragma warning disable CA2100, S2077 // statement originates from the checked-in provider script, not user input
                var command = new MySqlCommand(statement, connection);
#pragma warning restore CA2100, S2077
                await using (command.ConfigureAwait(false))
                {
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static bool IsCommentOrEmpty(string statement)
    {
        foreach (var line in statement.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    [Test]
    public async Task GetPendingAsync_WhenProcessingLeaseExpired_ReclaimsClaimedMessage(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (repository, timeProvider) = await CreateRepositoryAsync(cancellationToken).ConfigureAwait(false);

        var message = CreateMessage(timeProvider.GetUtcNow());
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(claimed).Count().IsEqualTo(1);

        // Simulate a crashed worker: the claimed message is never completed or failed.
        timeProvider.Advance(TimeSpan.FromMinutes(10));

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

        var (repository, timeProvider) = await CreateRepositoryAsync(cancellationToken).ConfigureAwait(false);

        var message = CreateMessage(timeProvider.GetUtcNow());
        await repository.AddAsync(message, cancellationToken).ConfigureAwait(false);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(claimed).Count().IsEqualTo(1);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var reclaimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(reclaimed).IsEmpty();
    }
}
