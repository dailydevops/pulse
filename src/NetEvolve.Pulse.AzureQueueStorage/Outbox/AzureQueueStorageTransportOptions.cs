namespace NetEvolve.Pulse.Outbox;

/// <summary>
/// Configuration options for <see cref="AzureQueueStorageMessageTransport"/>.
/// </summary>
public sealed class AzureQueueStorageTransportOptions
{
    /// <summary>
    /// Gets or sets the connection string used to authenticate against Azure Queue Storage.
    /// </summary>
    /// <remarks>
    /// When not provided, <see cref="QueueServiceUri"/> must be specified to use managed identity
    /// through <c>DefaultAzureCredential</c>.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the URI of the Azure Queue Storage service endpoint (e.g., <c>https://account.queue.core.windows.net</c>).
    /// </summary>
    /// <remarks>Required when <see cref="ConnectionString"/> is not supplied.</remarks>
    public Uri? QueueServiceUri { get; set; }

    /// <summary>
    /// Gets or sets the name of the queue to which outbox messages are sent.
    /// </summary>
    /// <remarks>Defaults to <c>pulse-outbox</c>.</remarks>
    public string QueueName { get; set; } = "pulse-outbox";

    /// <summary>
    /// Gets or sets the visibility timeout applied to each message sent to the queue.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, the queue's default visibility timeout is used.
    /// </remarks>
    public TimeSpan? MessageVisibilityTimeout { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the queue should be created automatically if it does not exist.
    /// </summary>
    /// <remarks>Defaults to <see langword="true"/>.</remarks>
    public bool CreateQueueIfNotExists { get; set; } = true;
}
