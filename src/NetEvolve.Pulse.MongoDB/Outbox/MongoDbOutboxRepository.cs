namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// MongoDB implementation of <see cref="IOutboxRepository"/> using the official MongoDB C# driver.
/// Provides atomic message claiming via <c>FindOneAndUpdateAsync</c> with
/// a sort on <c>CreatedAt</c> to prevent concurrent duplicate processing.
/// </summary>
/// <remarks>
/// <para><strong>Prerequisites:</strong></para>
/// The caller must register <see cref="IMongoClient"/> in the dependency injection container before
/// calling <c>UseMongoDbOutbox</c> or <c>AddMongoDbOutbox</c>.
/// <para><strong>Concurrency:</strong></para>
/// Each pending-message claim uses <c>FindOneAndUpdateAsync</c> to atomically transition one document
/// from <see cref="OutboxMessageStatus.Pending"/> to <see cref="OutboxMessageStatus.Processing"/>.
/// A batch is claimed by calling this operation up to <c>batchSize</c> times.
/// <para><strong>Claim Lease:</strong></para>
/// Each claim records the claim timestamp. Messages that remain in
/// <see cref="OutboxMessageStatus.Processing"/> longer than
/// <see cref="MongoDbOutboxOptions.ProcessingLeaseTimeout"/> (for example, after a worker crash or
/// host shutdown) are reclaimed by subsequent pending polls, preserving at-least-once delivery.
/// <para><strong>Date/Time Storage:</strong></para>
/// All <see cref="DateTimeOffset"/> values are stored as UTC <see cref="DateTime"/> in BSON. The UTC offset
/// is always zero when reading back from the database.
/// </remarks>
internal sealed class MongoDbOutboxRepository : IOutboxRepository
{
    /// <summary>The MongoDB client used to obtain database and collection references.</summary>
    private readonly IMongoClient _mongoClient;

    /// <summary>The time provider used to generate consistent timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The name of the MongoDB database that contains the outbox collection.</summary>
    private readonly string _databaseName;

    /// <summary>The name of the MongoDB collection used to store outbox documents.</summary>
    private readonly string _collectionName;

    /// <summary>Tracks whether the claim index has already been created by this instance (0 = no, 1 = yes).</summary>
    private int _claimIndexCreated;

    /// <summary>The maximum duration a claimed message may remain in the Processing status before it is reclaimed.</summary>
    private readonly TimeSpan _processingLeaseTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="MongoDbOutboxRepository"/> class.
    /// </summary>
    /// <param name="mongoClient">The MongoDB client used to access the outbox collection.</param>
    /// <param name="options">The MongoDB outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public MongoDbOutboxRepository(
        IMongoClient mongoClient,
        IOptions<MongoDbOutboxOptions> options,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(mongoClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var opts = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(opts.DatabaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(opts.CollectionName);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(opts.ProcessingLeaseTimeout, TimeSpan.Zero);

        _mongoClient = mongoClient;
        _timeProvider = timeProvider;
        _databaseName = opts.DatabaseName;
        _collectionName = opts.CollectionName;
        _processingLeaseTimeout = opts.ProcessingLeaseTimeout;
    }

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var doc = OutboxDocumentMapper.ToDocument(message);
        await GetCollection().InsertOneAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var leaseExpiredBefore = now - _processingLeaseTimeout;

        var pendingFilter = Builders<OutboxDocument>.Filter.And(
            Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.Pending),
            Builders<OutboxDocument>.Filter.Or(
                Builders<OutboxDocument>.Filter.Eq(d => d.NextRetryAt, null),
                Builders<OutboxDocument>.Filter.Lte(d => d.NextRetryAt, (DateTime?)now)
            )
        );

        var expiredLeaseFilter = Builders<OutboxDocument>.Filter.And(
            Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.Processing),
            Builders<OutboxDocument>.Filter.Or(
                Builders<OutboxDocument>.Filter.Lte(d => d.ProcessingStartedAt, (DateTime?)leaseExpiredBefore),
                Builders<OutboxDocument>.Filter.And(
                    Builders<OutboxDocument>.Filter.Eq(d => d.ProcessingStartedAt, null),
                    Builders<OutboxDocument>.Filter.Lte(d => d.UpdatedAt, leaseExpiredBefore)
                )
            )
        );

        var filter = Builders<OutboxDocument>.Filter.Or(pendingFilter, expiredLeaseFilter);

        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.Processing)
            .Set(d => d.UpdatedAt, now)
            .Set(d => d.ProcessingStartedAt, (DateTime?)now);

        var sort = Builders<OutboxDocument>.Sort.Ascending(d => d.CreatedAt);
        var findOptions = new FindOneAndUpdateOptions<OutboxDocument>
        {
            Sort = sort,
            ReturnDocument = ReturnDocument.After,
        };

        var messages = new List<OutboxMessage>(batchSize);
        var collection = await GetClaimCollectionAsync(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < batchSize; i++)
        {
            var doc = await collection
                .FindOneAndUpdateAsync(filter, update, findOptions, cancellationToken)
                .ConfigureAwait(false);

            if (doc is null)
            {
                break;
            }

            messages.Add(OutboxDocumentMapper.ToOutboxMessage(doc));
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = Builders<OutboxDocument>.Filter.And(
            Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.Failed),
            Builders<OutboxDocument>.Filter.Lt(d => d.RetryCount, maxRetryCount),
            Builders<OutboxDocument>.Filter.Or(
                Builders<OutboxDocument>.Filter.Eq(d => d.NextRetryAt, null),
                Builders<OutboxDocument>.Filter.Lte(d => d.NextRetryAt, (DateTime?)now)
            )
        );

        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.Processing)
            .Set(d => d.UpdatedAt, now)
            .Set(d => d.ProcessingStartedAt, (DateTime?)now);

        var sort = Builders<OutboxDocument>.Sort.Ascending(d => d.CreatedAt);
        var findOptions = new FindOneAndUpdateOptions<OutboxDocument>
        {
            Sort = sort,
            ReturnDocument = ReturnDocument.After,
        };

        var messages = new List<OutboxMessage>(batchSize);
        var collection = await GetClaimCollectionAsync(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < batchSize; i++)
        {
            var doc = await collection
                .FindOneAndUpdateAsync(filter, update, findOptions, cancellationToken)
                .ConfigureAwait(false);

            if (doc is null)
            {
                break;
            }

            messages.Add(OutboxDocumentMapper.ToOutboxMessage(doc));
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Id, messageId);
        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.Completed)
            .Set(d => d.UpdatedAt, now)
            .Set(d => d.ProcessedAt, now);

        _ = await GetCollection()
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Id, messageId);
        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.Failed)
            .Set(d => d.UpdatedAt, now)
            .Set(d => d.Error, errorMessage)
            .Inc(d => d.RetryCount, 1);

        _ = await GetCollection()
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
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
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Id, messageId);
        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.Failed)
            .Set(d => d.UpdatedAt, now)
            .Set(d => d.Error, errorMessage)
            .Inc(d => d.RetryCount, 1)
            .Set(d => d.NextRetryAt, nextRetryAt.HasValue ? nextRetryAt.Value.UtcDateTime : (DateTime?)null);

        _ = await GetCollection()
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsDeadLetterAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Id, messageId);
        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.DeadLetter)
            .Set(d => d.UpdatedAt, now)
            .Set(d => d.ProcessedAt, now)
            .Set(d => d.Error, errorMessage);

        _ = await GetCollection()
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.Pending);

        return await GetCollection()
            .CountDocumentsAsync(filter, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow().Subtract(olderThan).UtcDateTime;

        var filter = Builders<OutboxDocument>.Filter.And(
            Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.Completed),
            Builders<OutboxDocument>.Filter.Lte(d => d.ProcessedAt, (DateTime?)cutoff)
        );

        var result = await GetCollection()
            .DeleteManyAsync(filter, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return (int)result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var db = _mongoClient.GetDatabase(_databaseName);
            _ = await db.RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            return true;
        }
        catch (MongoException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the <see cref="IMongoCollection{TDocument}"/> for outbox documents.
    /// </summary>
    /// <returns>The outbox MongoDB collection.</returns>
    private IMongoCollection<OutboxDocument> GetCollection() =>
        _mongoClient.GetDatabase(_databaseName).GetCollection<OutboxDocument>(_collectionName);

    /// <summary>
    /// Returns the outbox collection and ensures a compound index on <c>Status</c> and <c>CreatedAt</c>
    /// exists, so each sorted claim operation avoids a full collection scan with an in-memory sort.
    /// The index is created at most once per repository instance; <c>createIndexes</c> is idempotent,
    /// so concurrent instances targeting the same collection are safe.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The outbox MongoDB collection.</returns>
    private async Task<IMongoCollection<OutboxDocument>> GetClaimCollectionAsync(CancellationToken cancellationToken)
    {
        var collection = GetCollection();

        if (Interlocked.CompareExchange(ref _claimIndexCreated, 1, 0) == 0)
        {
            try
            {
                var keys = Builders<OutboxDocument>.IndexKeys.Ascending(d => d.Status).Ascending(d => d.CreatedAt);
                _ = await collection
                    .Indexes.CreateOneAsync(
                        new CreateIndexModel<OutboxDocument>(keys),
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch
            {
                _ = Interlocked.Exchange(ref _claimIndexCreated, 0);
                throw;
            }
        }

        return collection;
    }
}
