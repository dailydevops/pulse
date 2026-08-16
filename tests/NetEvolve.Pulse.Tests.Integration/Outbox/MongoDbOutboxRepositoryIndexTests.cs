namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;
using TUnit.Core;

[TestGroup("MongoDB")]
public sealed class MongoDbOutboxRepositoryIndexTests
{
    [ClassDataSource<MongoDbContainerFixture>(Shared = SharedType.PerTestSession)]
    public required MongoDbContainerFixture Container { get; init; }

    private sealed record IndexTestEvent;

    private static OutboxMessage CreateMessage(DateTimeOffset createdAt) =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(IndexTestEvent),
            Payload = "{}",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            Status = OutboxMessageStatus.Pending,
        };

    [Test]
    public async Task GetPendingAsync_CreatesClaimIndexOnStatusAndCreatedAt(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var client = new MongoClient(Container.ConnectionString);
        var databaseName = $"pulse{Guid.NewGuid():N}";
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = databaseName }),
            TimeProvider.System
        );

        await repository.AddAsync(CreateMessage(DateTimeOffset.UtcNow), cancellationToken: cancellationToken);
        _ = await repository.GetPendingAsync(10, cancellationToken: cancellationToken);

        var collection = client
            .GetDatabase(databaseName)
            .GetCollection<OutboxDocument>(new MongoDbOutboxOptions().CollectionName);
        var indexes = await (await collection.Indexes.ListAsync(cancellationToken: cancellationToken)).ToListAsync(
            cancellationToken: cancellationToken
        );

        var hasClaimIndex = indexes.Any(index =>
        {
            var key = index["key"].AsBsonDocument;
            return key.ElementCount == 2
                && key.GetElement(0).Name == OutboxMessageSchema.Columns.Status
                && key.GetElement(1).Name == OutboxMessageSchema.Columns.CreatedAt;
        });

        _ = await Assert.That(hasClaimIndex).IsTrue();
    }

    [Test]
    public async Task GetFailedForRetryAsync_CreatesClaimIndexOnStatusAndCreatedAt(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var client = new MongoClient(Container.ConnectionString);
        var databaseName = $"pulse{Guid.NewGuid():N}";
        var repository = new MongoDbOutboxRepository(
            client,
            Options.Create(new MongoDbOutboxOptions { DatabaseName = databaseName }),
            TimeProvider.System
        );

        await repository.AddAsync(CreateMessage(DateTimeOffset.UtcNow), cancellationToken: cancellationToken);
        _ = await repository.GetFailedForRetryAsync(5, 10, cancellationToken: cancellationToken);

        var collection = client
            .GetDatabase(databaseName)
            .GetCollection<OutboxDocument>(new MongoDbOutboxOptions().CollectionName);
        var indexes = await (await collection.Indexes.ListAsync(cancellationToken: cancellationToken)).ToListAsync(
            cancellationToken: cancellationToken
        );

        var hasClaimIndex = indexes.Any(index =>
        {
            var key = index["key"].AsBsonDocument;
            return key.ElementCount == 2
                && key.GetElement(0).Name == OutboxMessageSchema.Columns.Status
                && key.GetElement(1).Name == OutboxMessageSchema.Columns.CreatedAt;
        });

        _ = await Assert.That(hasClaimIndex).IsTrue();
    }
}
