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

        var candidates = await ExecuteQueryAsync(query, cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return candidates;
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

        var candidates = await ExecuteQueryAsync(query, cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return candidates;
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

        using var iterator = _container.GetItemQueryIterator<long>(query);

        if (!iterator.HasMoreResults)
        {
            return 0L;
        }

        var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
        return response.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow().Subtract(olderThan);

        var query = new QueryDefinition(
            "SELECT c.id FROM c WHERE c.status = 2 AND c.processedAt <= @cutoff"
        ).WithParameter("@cutoff", cutoff);

        var deleted = 0;

        using var iterator = _container.GetItemQueryIterator<IdProjection>(query);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            foreach (var itemId in page.Select(x => x.Id))
            {
                try
                {
                    _ = await _container
                        .DeleteItemAsync<CosmosDbOutboxDocument>(
                            itemId,
                            new PartitionKey(itemId),
                            cancellationToken: cancellationToken
                        )
                        .ConfigureAwait(false);

                    deleted++;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // Already deleted by another worker — ignore.
                }
            }
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
    /// Executes a parameterized query and returns all matching documents as <see cref="OutboxMessage"/> instances.
    /// </summary>
    private async Task<IReadOnlyList<OutboxMessage>> ExecuteQueryAsync(
        QueryDefinition query,
        CancellationToken cancellationToken
    )
    {
        var messages = new List<OutboxMessage>();

        using var iterator = _container.GetItemQueryIterator<CosmosDbOutboxDocument>(query);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            foreach (var doc in page)
            {
                messages.Add(doc.ToOutboxMessage());
            }
        }

        return messages;
    }

    /// <summary>
    /// Attempts to atomically claim each candidate message by patching its status to
    /// <paramref name="targetStatus"/> using ETag-based optimistic concurrency.
    /// Messages that have been claimed by another worker (ETag mismatch) are silently skipped.
    /// </summary>
    private async Task<IReadOnlyList<OutboxMessage>> ClaimMessagesAsync(
        IReadOnlyList<OutboxMessage> candidates,
        int targetStatus,
        CancellationToken cancellationToken
    )
    {
        var now = _timeProvider.GetUtcNow();
        var claimed = new List<OutboxMessage>(candidates.Count);

        foreach (var message in candidates)
        {
            var id = message.Id.ToString();
            var partitionKey = new PartitionKey(id);

            // Read current ETag for optimistic concurrency.
            ItemResponse<CosmosDbOutboxDocument> current;

            try
            {
                current = await _container
                    .ReadItemAsync<CosmosDbOutboxDocument>(id, partitionKey, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Document deleted between query and read — skip.
                continue;
            }

            // Only claim if still in the expected status (Pending=0 or Failed=3).
            if (
                current.Resource.Status != message.Status.GetHashCode()
                && current.Resource.Status != (int)message.Status
            )
            {
                continue;
            }

            var requestOptions = new PatchItemRequestOptions { IfMatchEtag = current.ETag };

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
    private sealed class IdProjection
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
    }
}
