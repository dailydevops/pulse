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
public sealed class CosmosDbOutboxManagementGetDeadLetterMessageTests
{
    [Test]
    public async Task GetDeadLetterMessageAsync_WhenDocumentIsDeadLetter_ReturnsMessage(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageId = Guid.NewGuid();

        var container = new FakeCosmosContainer
        {
            OnReadItem = (id, _) =>
                new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(Guid.Parse(id), status: 4)),
        };

        var management = CreateManagement(container);

        var message = await management.GetDeadLetterMessageAsync(messageId, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(message).IsNotNull();
            _ = await Assert.That(message!.Id).IsEqualTo(messageId);
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WhenDocumentIsNotDeadLetter_ReturnsNull(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var container = new FakeCosmosContainer
        {
            OnReadItem = (id, _) =>
                new FakeItemResponse<CosmosDbOutboxDocument>(CreateDocument(Guid.Parse(id), status: 0)),
        };

        var management = CreateManagement(container);

        var message = await management
            .GetDeadLetterMessageAsync(Guid.NewGuid(), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(message).IsNull();
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WhenNotFound_ReturnsNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var container = new FakeCosmosContainer
        {
            OnReadItem = (_, _) => throw new CosmosException("gone", HttpStatusCode.NotFound, 0, "activity", 0),
        };

        var management = CreateManagement(container);

        var message = await management
            .GetDeadLetterMessageAsync(Guid.NewGuid(), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(message).IsNull();
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
