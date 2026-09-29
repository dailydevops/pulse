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
    /// Gets or sets the initial delay before a sent message becomes visible to consumers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <c>visibilitytimeout</c> of the Put Message operation: every message stays invisible to
    /// consumers for this duration after it was sent. It is not a processing lock or lease for consumers.
    /// When <see langword="null"/>, the service default of zero applies and messages are visible immediately.
    /// </para>
    /// <para>
    /// When set, the value must be between <see cref="TimeSpan.Zero"/> and 7 days (inclusive) and, for a finite
    /// <see cref="MessageTimeToLive"/>, smaller than the time-to-live. Azure Queue Storage uses whole seconds, so
    /// fractions of a second are truncated.
    /// </para>
    /// </remarks>
    public TimeSpan? MessageVisibilityTimeout { get; set; }

    /// <summary>
    /// Gets or sets the time-to-live of each message sent to the queue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <c>messagettl</c> of the Put Message operation. When <see langword="null"/>, the service
    /// default of 7 days applies, and messages that no consumer received within 7 days are deleted by the service
    /// although the outbox already marked them as delivered.
    /// </para>
    /// <para>
    /// When set, the value must be at least one second, or <see cref="NeverExpires"/> (or
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>) for messages that do not expire.
    /// Azure Queue Storage uses whole seconds, so fractions of a second are truncated.
    /// </para>
    /// </remarks>
    public TimeSpan? MessageTimeToLive { get; set; }

    /// <summary>
    /// The <see cref="MessageTimeToLive"/> value for messages that do not expire (<c>-1</c> second on the wire).
    /// </summary>
    public static readonly TimeSpan NeverExpires = TimeSpan.FromSeconds(-1);

    /// <summary>
    /// Gets or sets a value indicating whether the queue should be created automatically if it does not exist.
    /// </summary>
    /// <remarks>Defaults to <see langword="true"/>.</remarks>
    public bool CreateQueueIfNotExists { get; set; } = true;
}
