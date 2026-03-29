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

    /// <summary>
    /// Gets or sets whether to enable exponential backoff for failed messages.
    /// When <see langword="true"/>, failed messages are scheduled for retry after a computed delay
    /// based on <see cref="BaseRetryDelay"/>, <see cref="BackoffMultiplier"/>, and <see cref="MaxRetryDelay"/>.
    /// When <see langword="false"/> (the default), failed messages are eligible for retry on the next
    /// polling cycle and <see cref="OutboxMessage.NextRetryAt"/> is never set.
    /// Default: false.
    /// </summary>
    public bool EnableExponentialBackoff { get; set; }

    /// <summary>
    /// Gets or sets the base delay used as the starting point for exponential backoff calculations.
    /// The effective delay for retry attempt <c>n</c> is
    /// <c>BaseRetryDelay * BackoffMultiplier^n</c>, clamped to <see cref="MaxRetryDelay"/>.
    /// Only used when <see cref="EnableExponentialBackoff"/> is <see langword="true"/>.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum delay between retry attempts.
    /// The computed backoff delay is clamped to this value before being applied.
    /// Only used when <see cref="EnableExponentialBackoff"/> is <see langword="true"/>.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the multiplier applied to the base delay on each successive retry attempt.
    /// A value of <c>2.0</c> doubles the delay with each retry.
    /// Only used when <see cref="EnableExponentialBackoff"/> is <see langword="true"/>.
    /// Default: 2.0.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets whether to add random jitter to the computed backoff delay.
    /// When <see langword="true"/>, a uniformly distributed random value of up to 20% of the
    /// computed delay is added, reducing retry storms when many messages fail simultaneously.
    /// Only used when <see cref="EnableExponentialBackoff"/> is <see langword="true"/>.
    /// Default: true.
    /// </summary>
    public bool AddJitter { get; set; } = true;
}
