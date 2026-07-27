namespace NetEvolve.Pulse;

/// <summary>
/// Configuration options for the MongoDB outbox persistence provider.
/// </summary>
public sealed class MongoDbOutboxOptions
{
    /// <summary>
    /// Gets or sets the MongoDB database name to use for the outbox collection.
    /// </summary>
    /// <remarks>
    /// This value must be set before the outbox is used. The database is accessed
    /// via the <see cref="MongoDB.Driver.IMongoClient"/> registered in the dependency
    /// injection container.
    /// </remarks>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MongoDB collection name for outbox messages.
    /// Default: <c>outbox_messages</c>.
    /// </summary>
    public string CollectionName { get; set; } = "outbox_messages";

    /// <summary>
    /// Gets or sets the maximum duration a claimed message may remain in the
    /// <see cref="NetEvolve.Pulse.Extensibility.Outbox.OutboxMessageStatus.Processing"/> status
    /// before it becomes eligible for reclaiming by a subsequent pending poll.
    /// Default: 5 minutes.
    /// </summary>
    /// <remarks>
    /// When a worker crashes or is cancelled after claiming a message but before completing it,
    /// the message stays in the <c>Processing</c> status. Once this lease expires, the next pending
    /// poll claims the message again, preserving at-least-once delivery. Choose a value comfortably
    /// larger than the longest expected message dispatch duration to avoid duplicate publishing.
    /// </remarks>
    public TimeSpan ProcessingLeaseTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
