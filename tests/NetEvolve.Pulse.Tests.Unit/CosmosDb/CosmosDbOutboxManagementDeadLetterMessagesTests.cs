namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxManagementDeadLetterMessagesTests
{
    [Test]
    public async Task GetDeadLetterMessagesAsync_WithDocuments_MapsToOutboxMessages(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<CosmosDbOutboxDocument>([
                    [CreateDocument(firstId), CreateDocument(secondId)],
                ]),
        };

        using var client = new FakeCosmosClient(container);

        var management = new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var messages = await management
            .GetDeadLetterMessagesAsync(pageSize: 10, page: 0, cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(messages.Count).IsEqualTo(2);
            _ = await Assert.That(messages[0].Id).IsEqualTo(firstId);
            _ = await Assert.That(messages[0].Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
            _ = await Assert.That(messages[1].Id).IsEqualTo(secondId);
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithDefaultPaging_UsesConfiguredDefaults(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) => new FakeFeedIterator<CosmosDbOutboxDocument>([]),
        };

        using var client = new FakeCosmosClient(container);

        var management = new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var messages = await management
            .GetDeadLetterMessagesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(messages.Count).IsEqualTo(0);
    }

    private static CosmosDbOutboxDocument CreateDocument(Guid id) =>
        new CosmosDbOutboxDocument
        {
            Id = id.ToString(),
            EventType = typeof(string).AssemblyQualifiedName!,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = (int)OutboxMessageStatus.DeadLetter,
        };
}
