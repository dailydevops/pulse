namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Test double for <see cref="FeedIterator{T}"/> returning a fixed sequence of pages.
/// </summary>
internal sealed class FakeFeedIterator<T> : FeedIterator<T>
{
    private readonly Queue<IReadOnlyList<T>> _pages;

    public FakeFeedIterator(IReadOnlyList<IReadOnlyList<T>> pages) => _pages = new Queue<IReadOnlyList<T>>(pages);

    public override bool HasMoreResults => _pages.Count > 0;

    public override Task<FeedResponse<T>> ReadNextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<FeedResponse<T>>(new FakeFeedResponse<T>(_pages.Dequeue()));
}
