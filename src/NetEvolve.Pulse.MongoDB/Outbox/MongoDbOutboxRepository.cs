namespace NetEvolve.Pulse.Outbox;

using System.Diagnostics.CodeAnalysis;
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
/// <para><strong>Date/Time Storage:</strong></para>
/// All <see cref="DateTimeOffset"/> values are stored as UTC <see cref="DateTime"/> in BSON. The UTC offset
/// is always zero when reading back from the database.
/// </remarks>
[SuppressMessage(
    "Roslynator",
    "RCS1084:Use coalesce expression instead of conditional expression",
    Justification = "NextRetryAt and ProcessedAt properties require explicit conditional checks."
)]
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

        _mongoClient = mongoClient;
        _timeProvider = timeProvider;
        _databaseName = opts.DatabaseName;
        _collectionName = opts.CollectionName;
    }

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var doc = ToDocument(message);
        await GetCollection().InsertOneAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = Builders<OutboxDocument>.Filter.And(
            Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.Pending),
            Builders<OutboxDocument>.Filter.Or(
                Builders<OutboxDocument>.Filter.Eq<DateTime?>(d => d.NextRetryAt, null),
                Builders<OutboxDocument>.Filter.Lte(d => d.NextRetryAt, (DateTime?)now)
            )
        );

        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.Processing)
            .Set(d => d.UpdatedAt, now);

        var sort = Builders<OutboxDocument>.Sort.Ascending(d => d.CreatedAt);
        var findOptions = new FindOneAndUpdateOptions<OutboxDocument>
        {
            Sort = sort,
            ReturnDocument = ReturnDocument.After,
        };

        var messages = new List<OutboxMessage>(batchSize);
        var collection = GetCollection();

        for (var i = 0; i < batchSize; i++)
        {
            var doc = await collection
                .FindOneAndUpdateAsync(filter, update, findOptions, cancellationToken)
                .ConfigureAwait(false);

            if (doc is null)
            {
                break;
            }

            messages.Add(ToOutboxMessage(doc));
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
                Builders<OutboxDocument>.Filter.Eq<DateTime?>(d => d.NextRetryAt, null),
                Builders<OutboxDocument>.Filter.Lte(d => d.NextRetryAt, (DateTime?)now)
            )
        );

        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.Processing)
            .Set(d => d.UpdatedAt, now);

        var sort = Builders<OutboxDocument>.Sort.Ascending(d => d.CreatedAt);
        var findOptions = new FindOneAndUpdateOptions<OutboxDocument>
        {
            Sort = sort,
            ReturnDocument = ReturnDocument.After,
        };

        var messages = new List<OutboxMessage>(batchSize);
        var collection = GetCollection();

        for (var i = 0; i < batchSize; i++)
        {
            var doc = await collection
                .FindOneAndUpdateAsync(filter, update, findOptions, cancellationToken)
                .ConfigureAwait(false);

            if (doc is null)
            {
                break;
            }

            messages.Add(ToOutboxMessage(doc));
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
        catch
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
    /// Converts an <see cref="OutboxMessage"/> to an <see cref="OutboxDocument"/> for storage.
    /// </summary>
    /// <param name="message">The message to convert.</param>
    /// <returns>A new <see cref="OutboxDocument"/> populated from <paramref name="message"/>.</returns>
    private static OutboxDocument ToDocument(OutboxMessage message) =>
        new OutboxDocument
        {
            Id = message.Id,
            EventType = message.EventType.ToOutboxEventTypeName(),
            Payload = message.Payload,
            CorrelationId = message.CorrelationId,
            CausationId = message.CausationId,
            CreatedAt = message.CreatedAt.UtcDateTime,
            UpdatedAt = message.UpdatedAt.UtcDateTime,
            ProcessedAt = message.ProcessedAt.HasValue ? message.ProcessedAt.Value.UtcDateTime : (DateTime?)null,
            NextRetryAt = message.NextRetryAt.HasValue ? message.NextRetryAt.Value.UtcDateTime : (DateTime?)null,
            RetryCount = message.RetryCount,
            Error = message.Error,
            Status = (int)message.Status,
        };

    /// <summary>
    /// Converts an <see cref="OutboxDocument"/> retrieved from MongoDB to an <see cref="OutboxMessage"/>.
    /// </summary>
    /// <param name="doc">The document to convert.</param>
    /// <returns>A new <see cref="OutboxMessage"/> populated from <paramref name="doc"/>.</returns>
    private static OutboxMessage ToOutboxMessage(OutboxDocument doc) =>
        new OutboxMessage
        {
            Id = doc.Id,
            EventType =
                Type.GetType(doc.EventType)
                ?? throw new InvalidOperationException($"Cannot resolve event type '{doc.EventType}'."),
            Payload = doc.Payload,
            CorrelationId = doc.CorrelationId,
            CausationId = doc.CausationId,
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(doc.CreatedAt, DateTimeKind.Utc), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(doc.UpdatedAt, DateTimeKind.Utc), TimeSpan.Zero),
            ProcessedAt = doc.ProcessedAt.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(doc.ProcessedAt.Value, DateTimeKind.Utc), TimeSpan.Zero)
                : (DateTimeOffset?)null,
            NextRetryAt = doc.NextRetryAt.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(doc.NextRetryAt.Value, DateTimeKind.Utc), TimeSpan.Zero)
                : (DateTimeOffset?)null,
            RetryCount = doc.RetryCount,
            Error = doc.Error,
            Status = (OutboxMessageStatus)doc.Status,
        };
}
