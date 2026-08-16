namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxManagementReplayTests
{
    [Test]
    public async Task ReplayAllDeadLetterAsync_WithDeadLetterMessages_ReplaysWithoutPointReads(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var firstDocument = CreateDeadLetterDocument(Guid.NewGuid(), "\"etag-1\"");
        var secondDocument = CreateDeadLetterDocument(Guid.NewGuid(), "\"etag-2\"");
        var capturedEtags = new List<string?>();

        var container = new FakeCosmosContainer
        {
            OnPatchItem = (_, _, _, options) =>
            {
                capturedEtags.Add(options?.IfMatchEtag);
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDeadLetterDocument(Guid.NewGuid(), null));
            },
            OnQueryIterator = (itemType, _, _) => CreateIterator(itemType, [firstDocument, secondDocument]),
            OnReadItem = (id, _) =>
                new FakeItemResponse<CosmosDbOutboxDocument>(
                    CreateDeadLetterDocument(Guid.Parse(id), "\"read-etag\""),
                    "\"read-etag\""
                ),
        };

        using var client = new FakeCosmosClient(container);

        var management = new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var replayed = await management.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(replayed).IsEqualTo(2);
            _ = await Assert.That(container.PatchItemCalls).IsEqualTo(2);
            _ = await Assert.That(container.ReadItemCalls).IsEqualTo(0);
            _ = await Assert.That(capturedEtags.Count).IsEqualTo(2);
            _ = await Assert.That(capturedEtags[0]).IsEqualTo("\"etag-1\"");
            _ = await Assert.That(capturedEtags[1]).IsEqualTo("\"etag-2\"");
        }
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_WhenPatchThrowsPreconditionFailed_SkipsDocument(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = CreateDeadLetterDocument(Guid.NewGuid(), "\"etag\"");

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (itemType, _, _) => CreateIterator(itemType, [document]),
            OnPatchItem = (_, _, _, _) =>
                throw new global::Microsoft.Azure.Cosmos.CosmosException(
                    "conflict",
                    System.Net.HttpStatusCode.PreconditionFailed,
                    0,
                    "activity",
                    0
                ),
        };

        using var client = new FakeCosmosClient(container);

        var management = new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var replayed = await management.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(replayed).IsEqualTo(0);
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_WhenPatchThrowsNotFound_SkipsDocument(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = CreateDeadLetterDocument(Guid.NewGuid(), "\"etag\"");

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (itemType, _, _) => CreateIterator(itemType, [document]),
            OnPatchItem = (_, _, _, _) =>
                throw new global::Microsoft.Azure.Cosmos.CosmosException(
                    "gone",
                    System.Net.HttpStatusCode.NotFound,
                    0,
                    "activity",
                    0
                ),
        };

        using var client = new FakeCosmosClient(container);

        var management = new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var replayed = await management.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(replayed).IsEqualTo(0);
    }

    private static CosmosDbOutboxDocument CreateDeadLetterDocument(Guid id, string? etag) =>
        new CosmosDbOutboxDocument
        {
            Id = id.ToString(),
            EventType = typeof(string).AssemblyQualifiedName!,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = 4,
            ETag = etag,
        };

    private static object CreateIterator(Type itemType, IReadOnlyList<CosmosDbOutboxDocument> documents)
    {
        if (itemType == typeof(CosmosDbOutboxDocument))
        {
            return new FakeFeedIterator<CosmosDbOutboxDocument>([documents]);
        }

        // Legacy id-projection path: materialize instances of the requested projection type.
        var idProperty = itemType.GetProperty("Id");
        var page = Array.CreateInstance(itemType, documents.Count);

        for (var i = 0; i < documents.Count; i++)
        {
            var item = Activator.CreateInstance(itemType)!;
            idProperty!.SetValue(item, documents[i].Id);
            page.SetValue(item, i);
        }

        var pages = Array.CreateInstance(page.GetType(), 1);
        pages.SetValue(page, 0);

        return Activator.CreateInstance(typeof(FakeFeedIterator<>).MakeGenericType(itemType), [pages])!;
    }
}
