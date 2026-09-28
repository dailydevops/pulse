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
using NetEvolve.Pulse.Tests.Integration.Internals.Services;
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

        await MySqlScriptRunner
            .ExecuteAsync(connectionString, "OutboxMessage.sql", "OutboxMessage", tableName, cancellationToken)
            .ConfigureAwait(false);

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
