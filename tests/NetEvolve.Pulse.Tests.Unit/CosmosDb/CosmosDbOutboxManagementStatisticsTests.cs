namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxManagementStatisticsTests
{
    private sealed class StatusCount
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("count")]
        public long Count { get; set; }
    }

    [Test]
    public async Task GetStatisticsAsync_WithGroupedCounts_MapsEachStatus(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The management class' StatusCount projection is private; construct pages via the
        // reflection-friendly Activator pattern already used in CosmosDbOutboxManagementQueryOptionsTests.
        var container = new FakeCosmosContainer
        {
            OnQueryIterator = (itemType, _, _) => CreateStatusCountIterator(itemType),
        };

        using var client = new FakeCosmosClient(container);

        var management = new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var statistics = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(statistics.Pending).IsEqualTo(1L);
            _ = await Assert.That(statistics.Processing).IsEqualTo(2L);
            _ = await Assert.That(statistics.Completed).IsEqualTo(3L);
            _ = await Assert.That(statistics.Failed).IsEqualTo(4L);
            _ = await Assert.That(statistics.DeadLetter).IsEqualTo(5L);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_WithNoDocuments_ReturnsAllZeroes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var container = new FakeCosmosContainer { OnQueryIterator = (itemType, _, _) => CreateEmptyIterator(itemType) };

        using var client = new FakeCosmosClient(container);

        var management = new CosmosDbOutboxManagement(
            client,
            Options.Create(new CosmosDbOutboxOptions { DatabaseName = "TestDb" }),
            TimeProvider.System
        );

        var statistics = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(statistics.Pending).IsEqualTo(0L);
            _ = await Assert.That(statistics.Processing).IsEqualTo(0L);
            _ = await Assert.That(statistics.Completed).IsEqualTo(0L);
            _ = await Assert.That(statistics.Failed).IsEqualTo(0L);
            _ = await Assert.That(statistics.DeadLetter).IsEqualTo(0L);
        }
    }

    /// <summary>
    /// Builds a fake iterator page for the management class' private <c>StatusCount</c> projection type
    /// by serializing local <see cref="StatusCount"/> instances through JSON and deserializing into the
    /// target type, since both types share the same <c>status</c>/<c>count</c> JSON property names.
    /// </summary>
    private static object CreateStatusCountIterator(Type itemType)
    {
        var localCounts = new[]
        {
            new StatusCount { Status = (int)OutboxMessageStatus.Pending, Count = 1 },
            new StatusCount { Status = (int)OutboxMessageStatus.Processing, Count = 2 },
            new StatusCount { Status = (int)OutboxMessageStatus.Completed, Count = 3 },
            new StatusCount { Status = (int)OutboxMessageStatus.Failed, Count = 4 },
            new StatusCount { Status = (int)OutboxMessageStatus.DeadLetter, Count = 5 },
        };

        var page = Array.CreateInstance(itemType, localCounts.Length);

        for (var i = 0; i < localCounts.Length; i++)
        {
            var json = JsonSerializer.Serialize(localCounts[i]);
            var item = JsonSerializer.Deserialize(json, itemType);
            page.SetValue(item, i);
        }

        var pages = Array.CreateInstance(page.GetType(), 1);
        pages.SetValue(page, 0);

        return Activator.CreateInstance(typeof(FakeFeedIterator<>).MakeGenericType(itemType), [pages])!;
    }

    private static object CreateEmptyIterator(Type itemType)
    {
        var pageType = typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(itemType);
        var pagesListType = typeof(System.Collections.Generic.List<>).MakeGenericType(pageType);
        var pages = Activator.CreateInstance(pagesListType);
        var iteratorType = typeof(FakeFeedIterator<>).MakeGenericType(itemType);

        return Activator.CreateInstance(iteratorType, pages)!;
    }
}
