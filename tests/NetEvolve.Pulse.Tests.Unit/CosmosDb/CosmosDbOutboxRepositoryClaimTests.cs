namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
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
}
