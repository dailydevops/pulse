namespace NetEvolve.Pulse;

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
    /// Gets or sets the queue or topic name that receives outbox messages.
    /// </summary>
    public string EntityPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the transport should use batch sending for outbox batches.
    /// </summary>
    /// <remarks>Defaults to <see langword="true"/>.</remarks>
    public bool EnableBatching { get; set; } = true;
}
