namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxRepositoryQueryOptionsTests
{
    [Test]
    public async Task GetPendingAsync_PassesBoundedQueryRequestOptions(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) => new FakeFeedIterator<CosmosDbOutboxDocument>([]),
        };

        var repository = CreateRepository(container);

        _ = await repository.GetPendingAsync(25, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(container.CapturedQueryRequestOptions.Count).IsEqualTo(1);
            _ = await Assert.That(container.CapturedQueryRequestOptions[0]?.MaxItemCount).IsEqualTo(25);
            _ = await Assert.That(container.CapturedQueryRequestOptions[0]?.MaxConcurrency).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task GetFailedForRetryAsync_PassesBoundedQueryRequestOptions(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) => new FakeFeedIterator<CosmosDbOutboxDocument>([]),
        };

        var repository = CreateRepository(container);

        _ = await repository.GetFailedForRetryAsync(5, 42, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(container.CapturedQueryRequestOptions.Count).IsEqualTo(1);
            _ = await Assert.That(container.CapturedQueryRequestOptions[0]?.MaxItemCount).IsEqualTo(42);
            _ = await Assert.That(container.CapturedQueryRequestOptions[0]?.MaxConcurrency).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task GetPendingCountAsync_PassesParallelQueryRequestOptions(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<long>([
                    [0L],
                ]),
        };

        var repository = CreateRepository(container);

        _ = await repository.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(container.CapturedQueryRequestOptions.Count).IsEqualTo(1);
            _ = await Assert.That(container.CapturedQueryRequestOptions[0]?.MaxConcurrency).IsEqualTo(-1);
        }
    }

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
