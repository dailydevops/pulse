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
    /// Gets or sets the partition key path of the container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see cref="DefaultPartitionKeyPath"/> (<c>/id</c>), which is the only supported value.
    /// The container must be created with the partition key path <c>/id</c>, because the repository
    /// and the management API always use the document <c>id</c> as
    /// <see cref="Microsoft.Azure.Cosmos.PartitionKey"/> value for point reads, patches and deletes.
    /// Any other value fails options validation at host startup with an
    /// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/>. The repository and the
    /// management API also throw an <see cref="ArgumentException"/> when they are constructed with it.
    /// </para>
    /// <para>
    /// With the <c>/id</c> path every document is its own logical partition. Point
    /// operations stay single-partition, but the recurring status-based queries (pending polling,
    /// retry polling, counts, and cleanup) fan out to all physical partitions, so their RU cost
    /// and latency grow with the number of physical partitions. Enable
    /// <see cref="EnableTimeToLive"/> to keep the container small and the fan-out inexpensive.
    /// </para>
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

    /// <summary>
    /// Gets the validation error for <see cref="PartitionKeyPath"/>, or <see langword="null"/> when it is
    /// <see cref="DefaultPartitionKeyPath"/>.
    /// </summary>
    /// <remarks>
    /// Point operations always use the document <c>id</c> as partition key value. On a container
    /// partitioned on another path they return <c>404 Not Found</c>, which the claim and cleanup
    /// logic treats as "already deleted", so the outbox would stall silently.
    /// </remarks>
    internal string? PartitionKeyPathError =>
        string.Equals(PartitionKeyPath, DefaultPartitionKeyPath, StringComparison.Ordinal)
            ? null
            : $"{nameof(CosmosDbOutboxOptions)}.{nameof(PartitionKeyPath)} '{PartitionKeyPath}' is not supported. The only supported value is '{DefaultPartitionKeyPath}', and the container must be partitioned on '{DefaultPartitionKeyPath}'.";

    /// <summary>
    /// Throws when <see cref="PartitionKeyPath"/> is not <see cref="DefaultPartitionKeyPath"/>.
    /// </summary>
    /// <remarks>
    /// Fallback for options that bypass <see cref="CosmosDbOutboxOptionsValidator"/>, for example
    /// when the repository is constructed directly.
    /// </remarks>
    /// <exception cref="ArgumentException">The partition key path is not supported.</exception>
    internal void ThrowIfPartitionKeyPathIsNotSupported()
    {
        if (PartitionKeyPathError is { } error)
        {
            throw new ArgumentException(error);
        }
    }
}
