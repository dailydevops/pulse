namespace NetEvolve.Pulse.Outbox;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// MongoDB implementation of <see cref="IOutboxManagement"/>.
/// Provides dead-letter management and outbox statistics via the MongoDB C# driver.
/// </summary>
[SuppressMessage(
    "Roslynator",
    "RCS1084:Use coalesce expression instead of conditional expression",
    Justification = "ProcessedAt and NextRetryAt properties require explicit conditional checks."
)]
internal sealed class MongoDbOutboxManagement : IOutboxManagement
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
    /// Initializes a new instance of the <see cref="MongoDbOutboxManagement"/> class.
    /// </summary>
    /// <param name="mongoClient">The MongoDB client used to access the outbox collection.</param>
    /// <param name="options">The MongoDB outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public MongoDbOutboxManagement(
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
    public async Task<IReadOnlyList<OutboxMessage>> GetDeadLetterMessagesAsync(
        int pageSize = 50,
        int page = 0,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(page);

        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.DeadLetter);
        var sort = Builders<OutboxDocument>.Sort.Descending(d => d.CreatedAt);

        var docs = await GetCollection()
            .Find(filter)
            .Sort(sort)
            .Skip(page * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return docs.ConvertAll(ToOutboxMessage);
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetDeadLetterMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default
    )
    {
        var filter = Builders<OutboxDocument>.Filter.And(
            Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.DeadLetter),
            Builders<OutboxDocument>.Filter.Eq(d => d.Id, messageId)
        );

        var doc = await GetCollection().Find(filter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return doc is null ? null : ToOutboxMessage(doc);
    }

    /// <inheritdoc />
    public async Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.DeadLetter);

        return await GetCollection()
            .CountDocumentsAsync(filter, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ReplayMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = Builders<OutboxDocument>.Filter.And(
            Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.DeadLetter),
            Builders<OutboxDocument>.Filter.Eq(d => d.Id, messageId)
        );

        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.Pending)
            .Set(d => d.UpdatedAt, now)
            .Set(d => d.Error, (string?)null)
            .Set(d => d.ProcessedAt, (DateTime?)null)
            .Set(d => d.NextRetryAt, (DateTime?)null)
            .Set(d => d.RetryCount, 0);

        var result = await GetCollection()
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.ModifiedCount > 0;
    }

    /// <inheritdoc />
    public async Task<int> ReplayAllDeadLetterAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Status, (int)OutboxMessageStatus.DeadLetter);

        var update = Builders<OutboxDocument>
            .Update.Set(d => d.Status, (int)OutboxMessageStatus.Pending)
            .Set(d => d.UpdatedAt, now)
            .Set(d => d.Error, (string?)null)
            .Set(d => d.ProcessedAt, (DateTime?)null)
            .Set(d => d.NextRetryAt, (DateTime?)null)
            .Set(d => d.RetryCount, 0);

        var result = await GetCollection()
            .UpdateManyAsync(filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return (int)result.ModifiedCount;
    }

    /// <inheritdoc />
    public async Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var pipeline = new[]
        {
            new BsonDocument(
                "$group",
                new BsonDocument
                {
                    { "_id", BsonNull.Value },
                    { nameof(OutboxMessageStatus.Pending), StatusCountField(OutboxMessageStatus.Pending) },
                    { nameof(OutboxMessageStatus.Processing), StatusCountField(OutboxMessageStatus.Processing) },
                    { nameof(OutboxMessageStatus.Completed), StatusCountField(OutboxMessageStatus.Completed) },
                    { nameof(OutboxMessageStatus.Failed), StatusCountField(OutboxMessageStatus.Failed) },
                    { nameof(OutboxMessageStatus.DeadLetter), StatusCountField(OutboxMessageStatus.DeadLetter) },
                }
            ),
        };

        var aggregateResult = await GetCollection()
            .AggregateAsync<BsonDocument>(pipeline, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var firstElement = await aggregateResult.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (firstElement is null)
        {
            return new OutboxStatistics();
        }

        return new OutboxStatistics
        {
            Pending = firstElement[nameof(OutboxMessageStatus.Pending)].ToInt64(),
            Processing = firstElement[nameof(OutboxMessageStatus.Processing)].ToInt64(),
            Completed = firstElement[nameof(OutboxMessageStatus.Completed)].ToInt64(),
            Failed = firstElement[nameof(OutboxMessageStatus.Failed)].ToInt64(),
            DeadLetter = firstElement[nameof(OutboxMessageStatus.DeadLetter)].ToInt64(),
        };
    }

    /// <summary>
    /// Builds a <c>$sum</c>/<c>$cond</c> expression that counts documents whose status equals <paramref name="status"/>.
    /// </summary>
    private static BsonDocument StatusCountField(OutboxMessageStatus status) =>
        new BsonDocument(
            "$sum",
            new BsonDocument(
                "$cond",
                new BsonArray
                {
                    new BsonDocument("$eq", new BsonArray { "$" + OutboxMessageSchema.Columns.Status, (int)status }),
                    1,
                    0,
                }
            )
        );

    /// <summary>
    /// Returns the <see cref="IMongoCollection{TDocument}"/> for outbox documents.
    /// </summary>
    private IMongoCollection<OutboxDocument> GetCollection() =>
        _mongoClient.GetDatabase(_databaseName).GetCollection<OutboxDocument>(_collectionName);

    /// <summary>
    /// Converts an <see cref="OutboxDocument"/> retrieved from MongoDB to an <see cref="OutboxMessage"/>.
    /// </summary>
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
