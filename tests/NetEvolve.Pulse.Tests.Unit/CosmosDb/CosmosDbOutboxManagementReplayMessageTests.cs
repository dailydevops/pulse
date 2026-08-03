namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxManagementReplayMessageTests
{
    [Test]
    public async Task ReplayMessageAsync_WhenDocumentIsDeadLetter_PatchesToPendingAndReturnsTrue(
        CancellationToken cancellationToken
    )
    {
        var messageId = Guid.NewGuid();
        string? capturedEtag = null;

        var container = new FakeCosmosContainer
        {
            OnReadItem = (id, _) =>
                new FakeItemResponse<CosmosDbOutboxDocument>(
                    CreateDocument(Guid.Parse(id), status: 4),
                    "\"read-etag\""
                ),
            OnPatchItem = (_, _, _, options) =>
            {
                capturedEtag = options?.IfMatchEtag;
                return new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(messageId, status: 0));
            },
        };

        var management = CreateManagement(container);

        var result = await management.ReplayMessageAsync(messageId, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsTrue();
            _ = await Assert.That(container.PatchItemCalls).IsEqualTo(1);
            _ = await Assert.That(capturedEtag).IsEqualTo("\"read-etag\"");
        }
    }

    [Test]
    public async Task ReplayMessageAsync_WhenDocumentIsNotDeadLetter_ReturnsFalseWithoutPatching(
        CancellationToken cancellationToken
    )
    {
        var container = new FakeCosmosContainer
        {
            OnReadItem = (id, _) =>
                new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(Guid.Parse(id), status: 0)),
        };

        var management = CreateManagement(container);

        var result = await management.ReplayMessageAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsFalse();
            _ = await Assert.That(container.PatchItemCalls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ReplayMessageAsync_WhenNotFound_ReturnsFalse(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer
        {
            OnReadItem = (_, _) => throw new CosmosException("gone", HttpStatusCode.NotFound, 0, "activity", 0),
        };

        var management = CreateManagement(container);

        var result = await management.ReplayMessageAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ReplayMessageAsync_WhenPatchThrowsPreconditionFailed_ReturnsFalse(
        CancellationToken cancellationToken
    )
    {
        var container = new FakeCosmosContainer
        {
            OnReadItem = (id, _) =>
                new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(Guid.Parse(id), status: 4), "\"etag\""),
            OnPatchItem = (_, _, _, _) =>
                throw new CosmosException("conflict", HttpStatusCode.PreconditionFailed, 0, "activity", 0),
        };

        var management = CreateManagement(container);

        var result = await management.ReplayMessageAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsFalse();
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

    private static CosmosDbOutboxManagement CreateManagement(FakeCosmosContainer container)
    {
        using var client = new FakeCosmosClient(container);

        return new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );
    }
}
