namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxManagementQueryOptionsTests
{
    [Test]
    public async Task GetDeadLetterCountAsync_PassesParallelQueryRequestOptions(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) =>
                new FakeFeedIterator<long>([
                    [0L],
                ]),
        };

        var management = CreateManagement(container);

        _ = await management.GetDeadLetterCountAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(container.CapturedQueryRequestOptions.Count).IsEqualTo(1);
            _ = await Assert.That(container.CapturedQueryRequestOptions[0]?.MaxConcurrency).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_PassesParallelQueryRequestOptions(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer { OnQueryIterator = (itemType, _, _) => CreateEmptyIterator(itemType) };

        var management = CreateManagement(container);

        _ = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(container.CapturedQueryRequestOptions.Count).IsEqualTo(1);
            _ = await Assert.That(container.CapturedQueryRequestOptions[0]?.MaxConcurrency).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task ReplayAllDeadLetterAsync_PassesParallelQueryRequestOptions(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) => new FakeFeedIterator<CosmosDbOutboxDocument>([]),
        };

        var management = CreateManagement(container);

        _ = await management.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(container.CapturedQueryRequestOptions.Count).IsEqualTo(1);
            _ = await Assert.That(container.CapturedQueryRequestOptions[0]?.MaxConcurrency).IsEqualTo(-1);
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_PassesParallelQueryRequestOptions(CancellationToken cancellationToken)
    {
        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (_, _, _) => new FakeFeedIterator<CosmosDbOutboxDocument>([]),
        };

        var management = CreateManagement(container);

        _ = await management.GetDeadLetterMessagesAsync(50, 0, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(container.CapturedQueryRequestOptions.Count).IsEqualTo(1);
            _ = await Assert.That(container.CapturedQueryRequestOptions[0]?.MaxConcurrency).IsEqualTo(-1);
        }
    }

    /// <summary>
    /// Creates an empty <see cref="FakeFeedIterator{T}"/> for the requested item type via reflection,
    /// since some management queries use a private projection type that is inaccessible here.
    /// </summary>
    private static object CreateEmptyIterator(Type itemType)
    {
        var pageType = typeof(IReadOnlyList<>).MakeGenericType(itemType);
        var pagesListType = typeof(List<>).MakeGenericType(pageType);
        var pages = Activator.CreateInstance(pagesListType);
        var iteratorType = typeof(FakeFeedIterator<>).MakeGenericType(itemType);

        return Activator.CreateInstance(iteratorType, pages)!;
    }

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
