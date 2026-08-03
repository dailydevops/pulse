namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Scripts = Microsoft.Azure.Cosmos.Scripts.Scripts;

/// <summary>
/// Minimal test double for <see cref="CosmosClient"/> that returns a preconfigured container.
/// </summary>
internal sealed class FakeCosmosClient : CosmosClient
{
    private readonly Container _container;

    public FakeCosmosClient(Container container) => _container = container;

    public override Container GetContainer(string databaseId, string containerId) => _container;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2215:Dispose methods should call base class dispose",
        Justification = "The mocking constructor does not initialize the disposable client state."
    )]
    protected override void Dispose(bool disposing)
    {
        // Intentionally empty: the mock constructor does not initialize the disposable client state.
    }
}

/// <summary>
/// Minimal test double for <see cref="Container"/> with delegate hooks for the operations
/// exercised by the outbox repository and management implementations.
/// All other members throw <see cref="NotImplementedException"/>.
/// </summary>
internal sealed class FakeCosmosContainer : Container
{
    public Func<Type, QueryDefinition, QueryRequestOptions?, object>? OnQueryIterator { get; set; }

    public Func<string, PartitionKey, object>? OnReadItem { get; set; }

    public Func<
        string,
        PartitionKey,
        IReadOnlyList<PatchOperation>,
        PatchItemRequestOptions?,
        object
    >? OnPatchItem { get; set; }

    public Func<object, object>? OnCreateItem { get; set; }

    public Func<string, PartitionKey, object>? OnDeleteItem { get; set; }

    public Func<ContainerResponse?>? OnReadContainer { get; set; }

    public int ReadItemCalls { get; private set; }

    public int PatchItemCalls { get; private set; }

    public int QueryIteratorCalls { get; private set; }

    public List<QueryRequestOptions?> CapturedQueryRequestOptions { get; } = [];

    public override string Id => "fake";

    public override Database Database => throw new NotImplementedException();

    public override Conflicts Conflicts => throw new NotImplementedException();

    public override Scripts Scripts => throw new NotImplementedException();

    public override FeedIterator<T> GetItemQueryIterator<T>(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null
    )
    {
        QueryIteratorCalls++;
        CapturedQueryRequestOptions.Add(requestOptions);

        return OnQueryIterator is null
            ? throw new NotImplementedException()
            : (FeedIterator<T>)OnQueryIterator(typeof(T), queryDefinition, requestOptions);
    }

    public override FeedIterator<T> GetItemQueryIterator<T>(
        FeedRange feedRange,
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null
    ) => throw new NotImplementedException();

    public override FeedIterator<T> GetItemQueryIterator<T>(
        string? queryText = null,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null
    ) => throw new NotImplementedException();

    public override Task<ItemResponse<T>> ReadItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        ReadItemCalls++;

        return OnReadItem is null
            ? throw new NotImplementedException()
            : Task.FromResult((ItemResponse<T>)OnReadItem(id, partitionKey));
    }

    public override Task<ItemResponse<T>> PatchItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        IReadOnlyList<PatchOperation> patchOperations,
        PatchItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        PatchItemCalls++;

        return OnPatchItem is null
            ? throw new NotImplementedException()
            : Task.FromResult((ItemResponse<T>)OnPatchItem(id, partitionKey, patchOperations, requestOptions));
    }

    public override Task<ItemResponse<T>> CreateItemAsync<T>(
        T item,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) =>
        OnCreateItem is null
            ? throw new NotImplementedException()
            : Task.FromResult((ItemResponse<T>)OnCreateItem(item));

    public override Task<ItemResponse<T>> DeleteItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return OnDeleteItem is null
            ? throw new NotImplementedException()
            : Task.FromResult((ItemResponse<T>)OnDeleteItem(id, partitionKey));
    }

    public override Task<ResponseMessage> CreateItemStreamAsync(
        Stream streamPayload,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override TransactionalBatch CreateTransactionalBatch(PartitionKey partitionKey) =>
        throw new NotImplementedException();

    public override Task<ContainerResponse> DeleteContainerAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ResponseMessage> DeleteContainerStreamAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ResponseMessage> DeleteItemStreamAsync(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override ChangeFeedEstimator GetChangeFeedEstimator(string processorName, Container leaseContainer) =>
        throw new NotImplementedException();

    public override ChangeFeedProcessorBuilder GetChangeFeedEstimatorBuilder(
        string processorName,
        ChangesEstimationHandler estimationDelegate,
        TimeSpan? estimationPeriod = null
    ) => throw new NotImplementedException();

    public override FeedIterator<T> GetChangeFeedIterator<T>(
        ChangeFeedStartFrom changeFeedStartFrom,
        ChangeFeedMode changeFeedMode,
        ChangeFeedRequestOptions? changeFeedRequestOptions = null
    ) => throw new NotImplementedException();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder(
        string processorName,
        ChangeFeedStreamHandler onChangesDelegate
    ) => throw new NotImplementedException();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(
        string processorName,
        ChangeFeedHandler<T> onChangesDelegate
    ) => throw new NotImplementedException();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(
        string processorName,
        ChangesHandler<T> onChangesDelegate
    ) => throw new NotImplementedException();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint(
        string processorName,
        ChangeFeedStreamHandlerWithManualCheckpoint onChangesDelegate
    ) => throw new NotImplementedException();

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint<T>(
        string processorName,
        ChangeFeedHandlerWithManualCheckpoint<T> onChangesDelegate
    ) => throw new NotImplementedException();

    public override FeedIterator GetChangeFeedStreamIterator(
        ChangeFeedStartFrom changeFeedStartFrom,
        ChangeFeedMode changeFeedMode,
        ChangeFeedRequestOptions? changeFeedRequestOptions = null
    ) => throw new NotImplementedException();

    public override Task<IReadOnlyList<FeedRange>> GetFeedRangesAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public override IOrderedQueryable<T> GetItemLinqQueryable<T>(
        bool allowSynchronousQueryExecution = false,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null,
        CosmosLinqSerializerOptions? linqSerializerOptions = null
    ) => throw new NotImplementedException();

    public override FeedIterator GetItemQueryStreamIterator(
        FeedRange feedRange,
        QueryDefinition queryDefinition,
        string? continuationToken,
        QueryRequestOptions? requestOptions = null
    ) => throw new NotImplementedException();

    public override FeedIterator GetItemQueryStreamIterator(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null
    ) => throw new NotImplementedException();

    public override FeedIterator GetItemQueryStreamIterator(
        string? queryText = null,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null
    ) => throw new NotImplementedException();

    public override Task<ResponseMessage> PatchItemStreamAsync(
        string id,
        PartitionKey partitionKey,
        IReadOnlyList<PatchOperation> patchOperations,
        PatchItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ContainerResponse> ReadContainerAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => OnReadContainer is null ? throw new NotImplementedException() : Task.FromResult(OnReadContainer()!);

    public override Task<ResponseMessage> ReadContainerStreamAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ResponseMessage> ReadItemStreamAsync(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<FeedResponse<T>> ReadManyItemsAsync<T>(
        IReadOnlyList<(string id, PartitionKey partitionKey)> items,
        ReadManyRequestOptions? readManyRequestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ResponseMessage> ReadManyItemsStreamAsync(
        IReadOnlyList<(string id, PartitionKey partitionKey)> items,
        ReadManyRequestOptions? readManyRequestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<int?> ReadThroughputAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public override Task<ThroughputResponse> ReadThroughputAsync(
        RequestOptions requestOptions,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ContainerResponse> ReplaceContainerAsync(
        ContainerProperties containerProperties,
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ResponseMessage> ReplaceContainerStreamAsync(
        ContainerProperties containerProperties,
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ItemResponse<T>> ReplaceItemAsync<T>(
        T item,
        string id,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ResponseMessage> ReplaceItemStreamAsync(
        Stream streamPayload,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ThroughputResponse> ReplaceThroughputAsync(
        int throughput,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ThroughputResponse> ReplaceThroughputAsync(
        ThroughputProperties throughputProperties,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ItemResponse<T>> UpsertItemAsync<T>(
        T item,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public override Task<ResponseMessage> UpsertItemStreamAsync(
        Stream streamPayload,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();
}

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

/// <summary>
/// Test double for <see cref="FeedResponse{T}"/> wrapping an in-memory page.
/// </summary>
internal sealed class FakeFeedResponse<T> : FeedResponse<T>
{
    private readonly IReadOnlyList<T> _items;

    public FakeFeedResponse(IReadOnlyList<T> items) => _items = items;

    public override string ContinuationToken => throw new NotImplementedException();

    public override int Count => _items.Count;

    public override string IndexMetrics => throw new NotImplementedException();

    public override Headers Headers => new Headers();

    public override IEnumerable<T> Resource => _items;

    public override HttpStatusCode StatusCode => HttpStatusCode.OK;

    public override CosmosDiagnostics Diagnostics => throw new NotImplementedException();

    public override IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
}

/// <summary>
/// Test double for <see cref="ItemResponse{T}"/> exposing a resource and an ETag.
/// </summary>
internal sealed class FakeItemResponse<T> : ItemResponse<T>
{
    private readonly T _resource;
    private readonly string? _etag;

    public FakeItemResponse(T resource, string? etag = null)
    {
        _resource = resource;
        _etag = etag;
    }

    public override T Resource => _resource;

    public override string ETag => _etag!;

    public override Headers Headers => new Headers();

    public override HttpStatusCode StatusCode => HttpStatusCode.OK;

    public override CosmosDiagnostics Diagnostics => throw new NotImplementedException();

    public override double RequestCharge => 0;

    public override string ActivityId => string.Empty;
}
