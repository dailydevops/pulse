namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Configuration options for the outbox background processor.
/// </summary>
public sealed class OutboxProcessorOptions
{
    /// <summary>
    /// Gets or sets the maximum number of messages to process in a single batch.
    /// Default: 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the delay between processing cycles when no messages are found.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts before moving to dead letter.
    /// Default: 3.
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the timeout for processing a single message.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether to enable batch sending via <see cref="IMessageTransport.SendBatchAsync"/>.
    /// Default: false.
    /// </summary>
    public bool EnableBatchSending { get; set; }
}
