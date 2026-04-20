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
}
