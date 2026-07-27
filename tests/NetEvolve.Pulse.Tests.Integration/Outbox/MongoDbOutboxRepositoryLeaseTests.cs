namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;
using TUnit.Core;

[TestGroup("MongoDB")]
public sealed class MongoDbOutboxRepositoryLeaseTests
{
    [ClassDataSource<MongoDbContainerFixture>(Shared = SharedType.PerTestSession)]
    public required MongoDbContainerFixture Container { get; init; }

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

    private static (
        MongoDbOutboxRepository Repository,
        FakeTimeProvider TimeProvider,
        string DatabaseName
    ) CreateRepository(IMongoClient client)
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var databaseName = $"pulse{Guid.NewGuid():N}";
        var options = Options.Create(
            new MongoDbOutboxOptions { DatabaseName = databaseName, ProcessingLeaseTimeout = TimeSpan.FromMinutes(5) }
        );
        var repository = new MongoDbOutboxRepository(client, options, timeProvider);
        return (repository, timeProvider, databaseName);
    }

    [Test]
    public async Task GetPendingAsync_WhenProcessingLeaseExpired_ReclaimsClaimedMessage()
    {
        using var client = new MongoClient(Container.ConnectionString);
        var (repository, timeProvider, _) = CreateRepository(client);

        var message = CreateMessage(timeProvider.GetUtcNow());
        await repository.AddAsync(message);

        var claimed = await repository.GetPendingAsync(10);
        _ = await Assert.That(claimed).Count().IsEqualTo(1);

        // Simulate a crashed worker: the claimed message is never completed or failed.
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        var reclaimed = await repository.GetPendingAsync(10);

        _ = await Assert.That(reclaimed).Count().IsEqualTo(1);
        _ = await Assert.That(reclaimed[0].Id).IsEqualTo(message.Id);
    }

    [Test]
    public async Task GetPendingAsync_WhileProcessingLeaseActive_DoesNotReclaimClaimedMessage()
    {
        using var client = new MongoClient(Container.ConnectionString);
        var (repository, timeProvider, _) = CreateRepository(client);

        var message = CreateMessage(timeProvider.GetUtcNow());
        await repository.AddAsync(message);

        var claimed = await repository.GetPendingAsync(10);
        _ = await Assert.That(claimed).Count().IsEqualTo(1);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var reclaimed = await repository.GetPendingAsync(10);

        _ = await Assert.That(reclaimed).IsEmpty();
    }

    [Test]
    public async Task GetPendingAsync_WithLegacyProcessingDocumentWithoutLeaseField_ReclaimsAfterLeaseExpiry()
    {
        using var client = new MongoClient(Container.ConnectionString);
        var (repository, timeProvider, databaseName) = CreateRepository(client);

        var message = CreateMessage(timeProvider.GetUtcNow());
        await repository.AddAsync(message);

        var claimed = await repository.GetPendingAsync(10);
        _ = await Assert.That(claimed).Count().IsEqualTo(1);

        // Simulate a document claimed by a previous library version without lease tracking.
        var collection = client
            .GetDatabase(databaseName)
            .GetCollection<OutboxDocument>(new MongoDbOutboxOptions().CollectionName);
        var unset = Builders<OutboxDocument>.Update.Unset("ProcessingStartedAt");
        _ = await collection.UpdateOneAsync(Builders<OutboxDocument>.Filter.Eq(d => d.Id, message.Id), unset);

        timeProvider.Advance(TimeSpan.FromMinutes(10));

        var reclaimed = await repository.GetPendingAsync(10);

        _ = await Assert.That(reclaimed).Count().IsEqualTo(1);
        _ = await Assert.That(reclaimed[0].Id).IsEqualTo(message.Id);
    }
}
