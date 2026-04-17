namespace NetEvolve.Pulse.CosmosDb;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Azure Cosmos DB implementation of <see cref="IOutboxManagement"/> using the official
/// <c>Microsoft.Azure.Cosmos</c> SDK.
/// </summary>
/// <remarks>
/// Provides dead-letter inspection, message replay, and statistics queries against
/// the configured Cosmos DB container.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "await using statements in library code; ConfigureAwait applied to all Task-returning awaits."
)]
internal sealed class CosmosDbOutboxManagement : IOutboxManagement
{
    private readonly Container _container;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDbOutboxManagement"/> class.
    /// </summary>
    /// <param name="cosmosClient">The Cosmos DB client.</param>
    /// <param name="options">The Cosmos DB outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public CosmosDbOutboxManagement(
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
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetDeadLetterMessagesAsync(
        int pageSize = 50,
        int page = 0,
        CancellationToken cancellationToken = default
    )
    {
        var offset = page * pageSize;
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.status = 4 ORDER BY c.updatedAt DESC OFFSET @offset LIMIT @limit"
        )
            .WithParameter("@offset", offset)
            .WithParameter("@limit", pageSize);

        return await ExecuteQueryAsync(query, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetDeadLetterMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default
    )
    {
        var id = messageId.ToString();

        try
        {
            var response = await _container
                .ReadItemAsync<CosmosDbOutboxDocument>(id, new PartitionKey(id), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Resource.Status == (int)OutboxMessageStatus.DeadLetter
                ? response.Resource.ToOutboxMessage()
                : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.status = 4");

        using var iterator = _container.GetItemQueryIterator<long>(query);

        if (!iterator.HasMoreResults)
        {
            return 0L;
        }

        var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
        return response.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<bool> ReplayMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var id = messageId.ToString();
        var partitionKey = new PartitionKey(id);

        try
        {
            var current = await _container
                .ReadItemAsync<CosmosDbOutboxDocument>(id, partitionKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (current.Resource.Status != (int)OutboxMessageStatus.DeadLetter)
            {
                return false;
            }

            var now = _timeProvider.GetUtcNow();
            var requestOptions = new PatchItemRequestOptions { IfMatchEtag = current.ETag };

            var patches = new List<PatchOperation>
            {
                PatchOperation.Set("/status", (int)OutboxMessageStatus.Pending),
                PatchOperation.Set("/retryCount", 0),
                PatchOperation.Set("/error", (string?)null),
                PatchOperation.Set("/updatedAt", now),
            };

            _ = await _container
                .PatchItemAsync<CosmosDbOutboxDocument>(id, partitionKey, patches, requestOptions, cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> ReplayAllDeadLetterAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT c.id FROM c WHERE c.status = 4");
        var replayed = 0;

        using var iterator = _container.GetItemQueryIterator<IdProjection>(query);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            foreach (var item in page)
            {
                var replayed1 = await ReplayMessageAsync(Guid.Parse(item.Id), cancellationToken).ConfigureAwait(false);
                if (replayed1)
                {
                    replayed++;
                }
            }
        }

        return replayed;
    }

    /// <inheritdoc />
    public async Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT c.status, COUNT(1) AS count FROM c GROUP BY c.status");

        var counts = new Dictionary<int, long>();

        using var iterator = _container.GetItemQueryIterator<StatusCount>(query);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            foreach (var item in page)
            {
                counts[item.Status] = item.Count;
            }
        }

        return new OutboxStatistics
        {
            Pending = counts.GetValueOrDefault((int)OutboxMessageStatus.Pending),
            Processing = counts.GetValueOrDefault((int)OutboxMessageStatus.Processing),
            Completed = counts.GetValueOrDefault((int)OutboxMessageStatus.Completed),
            Failed = counts.GetValueOrDefault((int)OutboxMessageStatus.Failed),
            DeadLetter = counts.GetValueOrDefault((int)OutboxMessageStatus.DeadLetter),
        };
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
    /// Minimal projection used when querying only the document <c>id</c> field.
    /// </summary>
    private sealed class IdProjection
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Projection for grouped status counts.
    /// </summary>
    private sealed class StatusCount
    {
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public int Status { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("count")]
        public long Count { get; set; }
    }
}
