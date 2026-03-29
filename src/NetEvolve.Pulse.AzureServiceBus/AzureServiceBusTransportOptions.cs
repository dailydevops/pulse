namespace NetEvolve.Pulse;

/// <summary>
/// Configuration options for <see cref="AzureServiceBusMessageTransport"/>.
/// </summary>
public sealed class AzureServiceBusTransportOptions
{
    /// <summary>
    /// Gets or sets the Azure Service Bus connection string.
    /// </summary>
    /// <remarks>
    /// When set, the connection string is used for authentication.
    /// If not set, <see cref="FullyQualifiedNamespace"/> must be provided and
    /// <c>DefaultAzureCredential</c> is used for Managed Identity authentication.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified Service Bus namespace (e.g. <c>mynamespace.servicebus.windows.net</c>).
    /// </summary>
    /// <remarks>
    /// Used with <c>DefaultAzureCredential</c> when <see cref="ConnectionString"/> is not provided.
    /// </remarks>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>
    /// Gets or sets the name of the target queue or topic.
    /// </summary>
    public string EntityPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether batch sending is enabled.
    /// </summary>
    /// <remarks>
    /// When <c>true</c>, <see cref="AzureServiceBusMessageTransport.SendBatchAsync"/> uses
    /// <c>ServiceBusMessageBatch</c> for efficient bulk delivery. Defaults to <c>true</c>.
    /// </remarks>
    public bool EnableBatching { get; set; } = true;
}
