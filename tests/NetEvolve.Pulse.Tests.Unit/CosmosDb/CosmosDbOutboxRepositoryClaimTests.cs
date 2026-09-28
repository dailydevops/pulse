namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxRepositoryClaimTests
{
    [Test]
    public async Task GetPendingAsync_WithCandidates_ClaimsWithoutAdditionalPointRead(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        var document = CreateDocument(messageId, status: 0);
        document.ETag = "\"query-etag\"";
        var capturedOptions = new List<PatchItemRequestOptions?>();

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [document],
                ]),
            OnReadItem = (_, _) =>
                new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, status: 0), "\"read-etag\""),
            OnPatchItem = (_, _, _, options) =>
            {
                capturedOptions.Add(options);
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, status: 1));
            },
        };

        using var client = new FakeCosmosClient(container);

        var repository = new CosmosDbOutboxRepository(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(claimed.Count).IsEqualTo(1);
            _ = await Assert.That(container.PatchItemCalls).IsEqualTo(1);
            _ = await Assert.That(container.ReadItemCalls).IsEqualTo(0);
            _ = await Assert.That(capturedOptions.Count).IsEqualTo(1);
            _ = await Assert.That(capturedOptions[0]?.IfMatchEtag).IsEqualTo("\"query-etag\"");
        }
    }

    [Test]
    public async Task GetPendingAsync_WhenPatchThrowsPreconditionFailed_SkipsCandidate(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = CreateDocument(Guid.NewGuid(), status: 0);
        document.ETag = "\"etag\"";

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [document],
                ]),
            OnPatchItem = (_, _, _, _) =>
                throw new CosmosException("conflict", HttpStatusCode.PreconditionFailed, 0, "activity", 0),
        };

        var repository = CreateRepository(container);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(claimed.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetPendingAsync_WhenReclaimPatchThrowsPreconditionFailed_SkipsExpiredProcessingCandidate(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = CreateDocument(Guid.NewGuid(), status: 1);
        document.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        document.ETag = "\"stale-etag\"";
        var capturedOptions = new List<PatchItemRequestOptions?>();

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [document],
                ]),
            OnPatchItem = (_, _, _, options) =>
            {
                capturedOptions.Add(options);
                throw new CosmosException("conflict", HttpStatusCode.PreconditionFailed, 0, "activity", 0);
            },
        };

        var repository = CreateRepository(container);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(claimed.Count).IsEqualTo(0);
            _ = await Assert.That(capturedOptions.Count).IsEqualTo(1);
            _ = await Assert.That(capturedOptions[0]?.IfMatchEtag).IsEqualTo("\"stale-etag\"");
        }
    }

    [Test]
    public async Task GetPendingAsync_WhenPatchThrowsNotFound_SkipsCandidate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = CreateDocument(Guid.NewGuid(), status: 0);
        document.ETag = "\"etag\"";

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [document],
                ]),
            OnPatchItem = (_, _, _, _) => throw new CosmosException("gone", HttpStatusCode.NotFound, 0, "activity", 0),
        };

        var repository = CreateRepository(container);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(claimed.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetPendingAsync_WhenLaterPatchIsThrottled_ReturnsMessagesClaimedSoFar(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var claimedId = Guid.NewGuid();
        var throttledId = Guid.NewGuid();
        var first = CreateDocument(claimedId, status: 0);
        var second = CreateDocument(throttledId, status: 0);

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [first, second],
                ]),
            OnPatchItem = (id, _, _, _) =>
                id == claimedId.ToString()
                    ? new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(claimedId, status: 1))
                    : throw new CosmosException("throttled", HttpStatusCode.TooManyRequests, 0, "activity", 0),
        };

        var repository = CreateRepository(container);

        var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(claimed.Count).IsEqualTo(1);
            _ = await Assert.That(claimed[0].Id).IsEqualTo(claimedId);
        }
    }

    [Test]
    public async Task GetPendingAsync_QueriesExpiredProcessingLeases(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
        QueryDefinition? capturedQuery = null;

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, query, _) =>
            {
                capturedQuery = query;
                return new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [],
                ]);
            },
        };

        using var client = new FakeCosmosClient(container);
        var repository = new CosmosDbOutboxRepository(
            client,
            Options.Create(
                new CosmosDbOutboxOptions { DatabaseName = "TestDb", ProcessingLeaseTimeout = TimeSpan.FromMinutes(3) }
            ),
            timeProvider
        );

        _ = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

        var leaseExpiredBefore = capturedQuery!.GetQueryParameters().Single(p => p.Name == "@leaseExpiredBefore").Value;

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(capturedQuery.QueryText)
                .Contains("c.status = 1 AND c.updatedAt <= @leaseExpiredBefore");
            _ = await Assert.That(leaseExpiredBefore).IsEqualTo(timeProvider.GetUtcNow().AddMinutes(-3));
        }
    }

    [Test]
    public async Task GetPendingAsync_WhenFirstPatchIsThrottled_ThrowsCosmosException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = CreateDocument(Guid.NewGuid(), status: 0);

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [document],
                ]),
            OnPatchItem = (_, _, _, _) =>
                throw new CosmosException("throttled", HttpStatusCode.TooManyRequests, 0, "activity", 0),
        };

        var repository = CreateRepository(container);

        _ = await Assert
            .That(async () => await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false))
            .Throws<CosmosException>();
    }

    [Test]
    public async Task GetFailedForRetryAsync_WithCandidates_ClaimsAndReturnsMessages(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();
        var document = CreateDocument(messageId, status: 3);
        document.ETag = "\"etag\"";

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [document],
                ]),
            OnPatchItem = (_, _, _, _) =>
                new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, status: 1)),
        };

        var repository = CreateRepository(container);

        var claimed = await repository.GetFailedForRetryAsync(5, 10, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(claimed.Count).IsEqualTo(1);
    }

    private static CosmosDbOutboxDocument CreateDocument(Guid id, int status) =>
        new CosmosDbOutboxDocument
        {
            Id = id.ToString(),
            EventType = typeof(string).AssemblyQualifiedName!,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = status,
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
