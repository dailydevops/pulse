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
public sealed class CosmosDbOutboxRepositoryHealthTests
{
    [Test]
    public async Task IsHealthyAsync_WhenReadContainerSucceeds_ReturnsTrue(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer { OnReadContainer = () => null };
        var repository = CreateRepository(container);

        var result = await repository.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_WhenReadContainerThrowsCosmosException_ReturnsFalse(
        CancellationToken cancellationToken
    )
    {
        var container = new FakeCosmosContainer
        {
            OnReadContainer = () =>
                throw new CosmosException("unavailable", HttpStatusCode.ServiceUnavailable, 0, "activity", 0),
        };
        var repository = CreateRepository(container);

        var result = await repository.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsFalse();
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
