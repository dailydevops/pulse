namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxCountTests
{
    [Test]
    public async Task GetPendingCountAsync_WithMultiplePages_SumsAllPages(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<long>([
                    [],
                    [2L],
                    [3L],
                ]),
        };

        using var client = new FakeCosmosClient(container);

        var repository = new CosmosDbOutboxRepository(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var count = await repository.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(5L);
    }

    [Test]
    public async Task GetDeadLetterCountAsync_WithMultiplePages_SumsAllPages(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<long>([
                    [],
                    [4L],
                    [1L],
                ]),
        };

        using var client = new FakeCosmosClient(container);

        var management = new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var count = await management.GetDeadLetterCountAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(count).IsEqualTo(5L);
    }
}
