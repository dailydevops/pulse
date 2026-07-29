namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxRepositoryDeleteCompletedTests
{
    [Test]
    public async Task DeleteCompletedAsync_WithMultipleCandidates_DeletesEachQualifyingDocument(
        CancellationToken cancellationToken
    )
    {
        var expectedIds = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid().ToString()).ToList();
        var deletedIds = new ConcurrentBag<string>();

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxRepository.IdProjection>([
                    [.. expectedIds.Select(id => new CosmosDbOutboxRepository.IdProjection { Id = id })],
                ]),
            OnDeleteItem = (id, _) =>
            {
                deletedIds.Add(id);
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(id));
            },
        };

        var repository = CreateRepository(container);

        var deletedCount = await repository
            .DeleteCompletedAsync(TimeSpan.Zero, cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(deletedCount).IsEqualTo(expectedIds.Count);
            _ = await Assert.That(deletedIds.Count).IsEqualTo(expectedIds.Count);
            _ = await Assert.That(deletedIds.Distinct().Count()).IsEqualTo(expectedIds.Count);
            _ = await Assert.That(deletedIds.OrderBy(x => x)).IsEquivalentTo(expectedIds.OrderBy(x => x));
        }
    }

    [Test]
    public async Task DeleteCompletedAsync_WithNoCandidates_ReturnsZeroWithoutDeleting(
        CancellationToken cancellationToken
    )
    {
        var deleteCalls = 0;

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) => new FakeFeedIterator<CosmosDbOutboxRepository.IdProjection>([]),
            OnDeleteItem = (id, partitionKey) =>
            {
                _ = Interlocked.Increment(ref deleteCalls);
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(id));
            },
        };

        var repository = CreateRepository(container);

        var deletedCount = await repository
            .DeleteCompletedAsync(TimeSpan.Zero, cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(deletedCount).IsEqualTo(0);
            _ = await Assert.That(deleteCalls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task DeleteCompletedAsync_WithCanceledToken_ThrowsOperationCanceledException()
    {
        var deleteCalls = 0;

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxRepository.IdProjection>([
                    [new CosmosDbOutboxRepository.IdProjection { Id = Guid.NewGuid().ToString() }],
                ]),
            OnDeleteItem = (id, partitionKey) =>
            {
                _ = Interlocked.Increment(ref deleteCalls);
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(id));
            },
        };

        var repository = CreateRepository(container);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = await Assert
            .That(() => repository.DeleteCompletedAsync(TimeSpan.Zero, cts.Token))
            .Throws<OperationCanceledException>();

        _ = await Assert.That(deleteCalls).IsEqualTo(0);
    }

    private static CosmosDbOutboxDocument CreateDocument(string id) =>
        new CosmosDbOutboxDocument
        {
            Id = id,
            EventType = typeof(string).AssemblyQualifiedName!,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = 2,
        };

    private static CosmosDbOutboxRepository CreateRepository(FakeCosmosContainer container)
    {
        using var client = new FakeCosmosClient(container);

        return new CosmosDbOutboxRepository(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );
    }
}
