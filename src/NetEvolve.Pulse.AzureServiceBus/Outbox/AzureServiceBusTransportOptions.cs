namespace NetEvolve.Pulse.Outbox;

/// <summary>
/// Configuration options for <see cref="AzureServiceBusMessageTransport"/>.
/// </summary>
public sealed class AzureServiceBusTransportOptions
{
    /// <summary>
    /// Gets or sets the connection string used to authenticate against Azure Service Bus.
    /// </summary>
    /// <remarks>
    /// When not provided, <see cref="FullyQualifiedNamespace"/> must be specified to use managed identity
    /// through <c>DefaultAzureCredential</c>.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified namespace of the Azure Service Bus resource (e.g., <c>contoso.servicebus.windows.net</c>).
    /// </summary>
    /// <remarks>Required when <see cref="ConnectionString"/> is not supplied.</remarks>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the transport should use batch sending for outbox batches.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/>.
    /// When enabled, messages are grouped by their resolved topic or queue name for efficient batch sending.
    /// <para>
    /// If a queue or topic is partitioned (every entity in a partitioned Premium namespace is) and has
    /// duplicate detection enabled, Service Bus uses the <c>MessageId</c> as the partition key. Each outbox message has its own
    /// <c>MessageId</c>, and the service rejects any batch whose messages have different partition keys.
    /// The transport handles this by sending the rejected batch one message at a time and sending all later
    /// messages for that entity individually, which costs one rejected request per entity. For such entities,
    /// set this option to <see langword="false"/> to avoid that request.
    /// </para>
    /// </remarks>
    public bool EnableBatching { get; set; } = true;
}
