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
    /// All other documents (pending, processing, failed and replayed) get <c>ttl = -1</c>, so they never expire.
    /// <para>
    /// The container must have TTL enabled with <c>DefaultTimeToLive = -1</c> (on, no default) for this to take
    /// effect. A positive container default is not supported: documents written before this safeguard existed
    /// carry no <c>ttl</c> and expire after that many seconds, which loses undelivered messages.
    /// </para>
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
    /// Gets or sets the maximum duration a claimed message may remain in the
    /// <see cref="Extensibility.Outbox.OutboxMessageStatus.Processing"/> status
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
    /// Gets the validation error for <see cref="ProcessingLeaseTimeout"/>, or <see langword="null"/> when it is
    /// greater than <see cref="TimeSpan.Zero"/>.
    /// </summary>
    internal string? ProcessingLeaseTimeoutError =>
        ProcessingLeaseTimeout > TimeSpan.Zero
            ? null
            : $"{nameof(CosmosDbOutboxOptions)}.{nameof(ProcessingLeaseTimeout)} must be greater than zero, but was '{ProcessingLeaseTimeout}'.";

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
