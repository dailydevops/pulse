namespace NetEvolve.Pulse.Outbox;

/// <summary>
/// Configuration options for the Azure Cosmos DB outbox repository.
/// </summary>
public sealed class CosmosDbOutboxOptions
{
    /// <summary>
    /// Default container name used when <see cref="ContainerName"/> is not specified.
    /// </summary>
    public const string DefaultContainerName = "outbox_messages";

    /// <summary>
    /// Default partition key path used when <see cref="PartitionKeyPath"/> is not specified.
    /// </summary>
    public const string DefaultPartitionKeyPath = "/id";

    /// <summary>
    /// Default TTL in seconds (24 hours) used when <see cref="TtlSeconds"/> is not specified.
    /// </summary>
    public const int DefaultTtlSeconds = 86400;

    /// <summary>
    /// Gets or sets the Cosmos DB database name.
    /// </summary>
    /// <remarks>
    /// This property is required. The database must already exist before using this provider.
    /// </remarks>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Cosmos DB container name.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="DefaultContainerName"/> (<c>outbox_messages</c>).
    /// The container must already exist before using this provider.
    /// </remarks>
    public string ContainerName { get; set; } = DefaultContainerName;

    /// <summary>
    /// Gets or sets the partition key path for the container.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="DefaultPartitionKeyPath"/> (<c>/id</c>).
    /// This value is informational for container creation guidance; the repository uses it
    /// to construct <see cref="Microsoft.Azure.Cosmos.PartitionKey"/> values for point operations.
    /// </remarks>
    public string PartitionKeyPath { get; set; } = DefaultPartitionKeyPath;

    /// <summary>
    /// Gets or sets a value indicating whether TTL is enabled for completed and dead-letter documents.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, the <c>ttl</c> property is set to <see cref="TtlSeconds"/>
    /// on documents that transition to <see cref="Extensibility.Outbox.OutboxMessageStatus.Completed"/>
    /// or <see cref="Extensibility.Outbox.OutboxMessageStatus.DeadLetter"/> status,
    /// enabling automatic cleanup by the Cosmos DB TTL engine.
    /// The container must have TTL enabled (DefaultTimeToLive set) for this to take effect.
    /// </remarks>
    public bool EnableTimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the TTL in seconds for completed and dead-letter documents.
    /// </summary>
    /// <remarks>
    /// Only applies when <see cref="EnableTimeToLive"/> is <see langword="true"/>.
    /// Defaults to <see cref="DefaultTtlSeconds"/> (86400 seconds = 24 hours).
    /// </remarks>
    public int TtlSeconds { get; set; } = DefaultTtlSeconds;
}
