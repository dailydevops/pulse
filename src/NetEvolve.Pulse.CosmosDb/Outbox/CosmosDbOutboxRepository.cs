namespace NetEvolve.Pulse.Outbox;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using Newtonsoft.Json;

/// <summary>
/// Azure Cosmos DB implementation of <see cref="IOutboxRepository"/> using the official
/// <c>Microsoft.Azure.Cosmos</c> SDK.
/// </summary>
/// <remarks>
/// <para><strong>Concurrency:</strong></para>
/// Uses ETag-based conditional patch to atomically claim pending messages
/// and prevent duplicate processing by concurrent workers.
/// <para><strong>TTL Support:</strong></para>
/// When <see cref="CosmosDbOutboxOptions.EnableTimeToLive"/> is <see langword="true"/>,
/// the <c>ttl</c> field is set on completed and dead-letter documents so the Cosmos DB
/// TTL engine removes them automatically after <see cref="CosmosDbOutboxOptions.TtlSeconds"/> seconds.
/// <para><strong>Query fan-out:</strong></para>
/// With the default partition key path <c>/id</c> every document forms its own logical partition,
/// so the recurring status-polling and count queries cannot target a single partition and fan out
/// to all physical partitions. The queries are issued with bounded page sizes and maximum
/// parallelism to limit the cost; keeping the container small (see
/// <see cref="CosmosDbOutboxOptions.EnableTimeToLive"/>) prevents the fan-out from growing with
/// accumulated completed documents.
/// <para><strong>Prerequisites:</strong></para>
/// The caller must register a <see cref="CosmosClient"/> in the DI container before calling the
/// registration extension methods.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
internal sealed class CosmosDbOutboxRepository : IOutboxRepository
{
    private readonly Container _container;
    private readonly TimeProvider _timeProvider;
    private readonly bool _enableTtl;
    private readonly int _ttlSeconds;

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDbOutboxRepository"/> class.
    /// </summary>
    /// <param name="cosmosClient">The Cosmos DB client.</param>
    /// <param name="options">The Cosmos DB outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public CosmosDbOutboxRepository(
        CosmosClient cosmosClient,
        IOptions<CosmosDbOutboxOptions> options,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var opts = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(opts.DatabaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(opts.ContainerName);

        _container = cosmosClient.GetContainer(opts.DatabaseName, opts.ContainerName);
        _timeProvider = timeProvider;
        _enableTtl = opts.EnableTimeToLive;
        _ttlSeconds = opts.TtlSeconds;
    }

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var document = CosmosDbOutboxDocument.FromOutboxMessage(message);

        _ = await _container.CreateItemAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.status = 0 AND (IS_NULL(c.nextRetryAt) OR c.nextRetryAt <= @now) ORDER BY c._ts ASC OFFSET 0 LIMIT @batchSize"
        )
            .WithParameter("@now", now)
            .WithParameter("@batchSize", batchSize);

        var candidates = await ExecuteQueryAsync(query, CreateBatchQueryOptions(batchSize), cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return [];
        }

        return await ClaimMessagesAsync(candidates, (int)OutboxMessageStatus.Processing, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.status = 3 AND c.retryCount < @maxRetryCount AND (IS_NULL(c.nextRetryAt) OR c.nextRetryAt <= @now) ORDER BY c._ts ASC OFFSET 0 LIMIT @batchSize"
        )
            .WithParameter("@maxRetryCount", maxRetryCount)
            .WithParameter("@now", now)
            .WithParameter("@batchSize", batchSize);

        var candidates = await ExecuteQueryAsync(query, CreateBatchQueryOptions(batchSize), cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return [];
        }

        return await ClaimMessagesAsync(candidates, (int)OutboxMessageStatus.Processing, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var id = messageId.ToString();
        var partitionKey = new PartitionKey(id);

        var patches = new List<PatchOperation>
        {
            PatchOperation.Set("/status", (int)OutboxMessageStatus.Completed),
            PatchOperation.Set("/updatedAt", now),
            PatchOperation.Set("/processedAt", now),
        };

        if (_enableTtl)
        {
            patches.Add(PatchOperation.Set("/ttl", _ttlSeconds));
        }

        _ = await _container
            .PatchItemAsync<CosmosDbOutboxDocument>(id, partitionKey, patches, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();
        var id = messageId.ToString();
        var partitionKey = new PartitionKey(id);

        var patches = new List<PatchOperation>
        {
            PatchOperation.Set("/status", (int)OutboxMessageStatus.Failed),
            PatchOperation.Set("/updatedAt", now),
            PatchOperation.Set("/error", errorMessage),
            PatchOperation.Increment("/retryCount", 1),
        };

        _ = await _container
            .PatchItemAsync<CosmosDbOutboxDocument>(id, partitionKey, patches, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        DateTimeOffset? nextRetryAt,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();
        var id = messageId.ToString();
        var partitionKey = new PartitionKey(id);

        var patches = new List<PatchOperation>
        {
            PatchOperation.Set("/status", (int)OutboxMessageStatus.Failed),
            PatchOperation.Set("/updatedAt", now),
            PatchOperation.Set("/error", errorMessage),
            PatchOperation.Increment("/retryCount", 1),
            PatchOperation.Set("/nextRetryAt", nextRetryAt),
        };

        _ = await _container
            .PatchItemAsync<CosmosDbOutboxDocument>(id, partitionKey, patches, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsDeadLetterAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();
        var id = messageId.ToString();
        var partitionKey = new PartitionKey(id);

        var patches = new List<PatchOperation>
        {
            PatchOperation.Set("/status", (int)OutboxMessageStatus.DeadLetter),
            PatchOperation.Set("/updatedAt", now),
            PatchOperation.Set("/processedAt", now),
            PatchOperation.Set("/error", errorMessage),
        };

        if (_enableTtl)
        {
            patches.Add(PatchOperation.Set("/ttl", _ttlSeconds));
        }

        _ = await _container
            .PatchItemAsync<CosmosDbOutboxDocument>(id, partitionKey, patches, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.status = 0");

        using var iterator = _container.GetItemQueryIterator<long>(
            query,
            requestOptions: new QueryRequestOptions { MaxConcurrency = -1 }
        );

        var count = 0L;

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            count += response.Sum();
        }

        return count;
    }

    /// <summary>
    /// Upper bound on the number of concurrent <c>DeleteItemAsync</c> calls issued by
    /// <see cref="DeleteCompletedAsync"/>. Kept small and conservative to avoid overwhelming the
    /// container's provisioned throughput while still avoiding fully sequential round trips.
    /// </summary>
    private const int MaxDeleteConcurrency = 8;

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow().Subtract(olderThan);

        var query = new QueryDefinition(
            "SELECT c.id FROM c WHERE c.status = 2 AND c.processedAt <= @cutoff"
        ).WithParameter("@cutoff", cutoff);

        var deleted = 0;

        using var iterator = _container.GetItemQueryIterator<IdProjection>(
            query,
            requestOptions: new QueryRequestOptions { MaxConcurrency = -1 }
        );

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            await Parallel
                .ForEachAsync(
                    page.Select(x => x.Id),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaxDeleteConcurrency,
                        CancellationToken = cancellationToken,
                    },
                    async (itemId, itemCancellationToken) =>
                    {
                        try
                        {
                            _ = await _container
                                .DeleteItemAsync<CosmosDbOutboxDocument>(
                                    itemId,
                                    new PartitionKey(itemId),
                                    cancellationToken: itemCancellationToken
                                )
                                .ConfigureAwait(false);

                            _ = Interlocked.Increment(ref deleted);
                        }
                        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                        {
                            // Already deleted by another worker — ignore.
                        }
                    }
                )
                .ConfigureAwait(false);
        }

        return deleted;
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await _container.ReadContainerAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (CosmosException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates <see cref="QueryRequestOptions"/> for the recurring status-polling queries.
    /// The queries filter on <c>c.status</c> without a partition key and therefore fan out to
    /// every physical partition; bounding the page size to the batch size and enabling maximum
    /// parallelism keeps the per-poll latency and RU cost as low as possible.
    /// </summary>
    private static QueryRequestOptions CreateBatchQueryOptions(int batchSize) =>
        new QueryRequestOptions { MaxItemCount = batchSize, MaxConcurrency = -1 };

    /// <summary>
    /// Executes a parameterized query and returns all matching documents.
    /// </summary>
    private async Task<IReadOnlyList<CosmosDbOutboxDocument>> ExecuteQueryAsync(
        QueryDefinition query,
        QueryRequestOptions requestOptions,
        CancellationToken cancellationToken
    )
    {
        var documents = new List<CosmosDbOutboxDocument>();

        using var iterator = _container.GetItemQueryIterator<CosmosDbOutboxDocument>(
            query,
            requestOptions: requestOptions
        );

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            documents.AddRange(page);
        }

        return documents;
    }

    /// <summary>
    /// Attempts to atomically claim each candidate document by patching its status to
    /// <paramref name="targetStatus"/> using the <c>_etag</c> returned by the candidate query
    /// as an <c>IfMatchEtag</c> precondition, avoiding an additional point read per candidate.
    /// Messages that have been modified by another worker (ETag mismatch) are silently skipped.
    /// </summary>
    private async Task<IReadOnlyList<OutboxMessage>> ClaimMessagesAsync(
        IReadOnlyList<CosmosDbOutboxDocument> candidates,
        int targetStatus,
        CancellationToken cancellationToken
    )
    {
        var now = _timeProvider.GetUtcNow();
        var claimed = new List<OutboxMessage>(candidates.Count);

        foreach (var document in candidates)
        {
            var id = document.Id;
            var partitionKey = new PartitionKey(id);

            var requestOptions = new PatchItemRequestOptions { IfMatchEtag = document.ETag };

            var patches = new List<PatchOperation>
            {
                PatchOperation.Set("/status", targetStatus),
                PatchOperation.Set("/updatedAt", now),
            };

            try
            {
                var patched = await _container
                    .PatchItemAsync<CosmosDbOutboxDocument>(
                        id,
                        partitionKey,
                        patches,
                        requestOptions,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                claimed.Add(patched.Resource.ToOutboxMessage());
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // Another worker claimed this message — skip.
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Document deleted between read and patch — skip.
            }
        }

        return claimed;
    }

    /// <summary>
    /// Minimal projection used when querying only the document <c>id</c> field.
    /// </summary>
    /// <remarks>
    /// Internal (rather than private) so unit tests can construct fake query results of this
    /// exact shape without going through a live Cosmos DB query.
    /// </remarks>
    internal sealed class IdProjection
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
    }
}
