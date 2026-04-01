namespace NetEvolve.Pulse.Outbox;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using NetEvolve.Pulse.Extensibility.Outbox;

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
    /// Gets or sets whether to enable exponential backoff with jitter for failed message retries.
    /// When enabled, failed messages are not retried before their scheduled <see cref="OutboxMessage.NextRetryAt"/> time.
    /// Default: false.
    /// </summary>
    public bool EnableExponentialBackoff { get; set; }

    /// <summary>
    /// Gets or sets the base delay for the first retry attempt when exponential backoff is enabled.
    /// Default: 5 seconds.
    /// </summary>
    /// <remarks>
    /// The actual retry delay is computed as: <c>BaseRetryDelay * (BackoffMultiplier ^ RetryCount) + jitter</c>,
    /// clamped to <see cref="MaxRetryDelay"/>.
    /// </remarks>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum delay between retry attempts when exponential backoff is enabled.
    /// Default: 5 minutes.
    /// </summary>
    /// <remarks>
    /// Computed retry delays are clamped to not exceed this value, preventing indefinite growth.
    /// </remarks>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the multiplier applied to the delay for each retry iteration when exponential backoff is enabled.
    /// Default: 2.0 (doubles the delay each time).
    /// </summary>
    /// <remarks>
    /// For example, with <see cref="BaseRetryDelay"/> of 5 seconds and <see cref="BackoffMultiplier"/> of 2.0:
    /// <list type="bullet">
    /// <item><description>Retry 0: 5 seconds</description></item>
    /// <item><description>Retry 1: 10 seconds</description></item>
    /// <item><description>Retry 2: 20 seconds</description></item>
    /// <item><description>Retry 3+: clamped to <see cref="MaxRetryDelay"/></description></item>
    /// </list>
    /// </remarks>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets whether to add random jitter to computed backoff delays.
    /// When enabled, adds up to 20% jitter to help avoid thundering herd problems.
    /// Default: true.
    /// </summary>
    /// <remarks>
    /// Jitter is computed as a random value up to 20% of the computed base delay.
    /// This helps prevent multiple instances from retrying simultaneously after a service recovery.
    /// </remarks>
    public bool AddJitter { get; set; } = true;

    /// <summary>
    /// Gets per-event-type configuration overrides via a thread-safe concurrent dictionary.
    /// </summary>
    /// <remarks>
    /// <para><strong>Concurrency Guarantees:</strong></para>
    /// The property uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread-safe access.
    /// This is required because the dictionary may be read concurrently during <see cref="OutboxProcessorHostedService.ProcessBatchAsync(CancellationToken)"/>
    /// while being modified externally through code or dependency injection configuration.
    /// <para><strong>Dictionary Key Format:</strong></para>
    /// The dictionary key must match the <see cref="OutboxMessage.EventType"/> value of the messages
    /// to be overridden. Use the runtime <see cref="Type"/> of the event class directly.
    /// <para><strong>Override Precedence:</strong></para>
    /// Any non-<c>null</c> property on the associated <see cref="OutboxEventTypeOptions"/> takes
    /// precedence over the corresponding global default for messages of that event type.
    /// Properties left as <c>null</c> fall back to the global default.
    /// <para><strong>Stored but Unapplied Properties:</strong></para>
    /// </remarks>
    public ConcurrentDictionary<Type, OutboxEventTypeOptions> EventTypeOverrides { get; } =
        new ConcurrentDictionary<Type, OutboxEventTypeOptions>();

    /// <summary>
    /// Returns the effective <see cref="MaxRetryCount"/> for the given event type,
    /// applying any configured per-type override.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns>The resolved maximum retry count.</returns>
    internal int GetEffectiveMaxRetryCount(Type eventType) =>
        EventTypeOverrides.TryGetValue(eventType, out var overrides) && overrides.MaxRetryCount.HasValue
            ? overrides.MaxRetryCount.Value
            : MaxRetryCount;

    /// <summary>
    /// Returns the effective <see cref="ProcessingTimeout"/> for the given event type,
    /// applying any configured per-type override.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns>The resolved processing timeout.</returns>
    internal TimeSpan GetEffectiveProcessingTimeout(Type eventType) =>
        EventTypeOverrides.TryGetValue(eventType, out var overrides) && overrides.ProcessingTimeout.HasValue
            ? overrides.ProcessingTimeout.Value
            : ProcessingTimeout;

    /// <summary>
    /// Returns the effective <see cref="EnableBatchSending"/> value for the given event type,
    /// applying any configured per-type override.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns>The resolved batch sending flag.</returns>
    internal bool GetEffectiveEnableBatchSending(Type eventType) =>
        EventTypeOverrides.TryGetValue(eventType, out var overrides) && overrides.EnableBatchSending.HasValue
            ? overrides.EnableBatchSending.Value
            : EnableBatchSending;

    /// <summary>
    /// Computes the next retry timestamp for a failed message using exponential backoff with optional jitter.
    /// </summary>
    /// <param name="now">The current timestamp.</param>
    /// <param name="retryCount">The number of retries already attempted (0-based).</param>
    /// <returns>The computed <see cref="DateTimeOffset"/> for the next retry attempt.</returns>
    /// <remarks>
    /// <para><strong>Formula:</strong></para>
    /// <c>now + min(BaseRetryDelay * BackoffMultiplier^RetryCount + jitter, MaxRetryDelay)</c>
    /// <para><strong>Jitter:</strong></para>
    /// When <see cref="AddJitter"/> is true, adds a random value up to 20% of the computed base delay.
    /// </remarks>
    internal DateTimeOffset ComputeNextRetryAt(DateTimeOffset now, int retryCount)
    {
        try
        {
            // Compute base delay: BaseRetryDelay * (BackoffMultiplier ^ RetryCount)
            var baseDelayMs = BaseRetryDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, retryCount);

            // Guard against overflow
            if (double.IsInfinity(baseDelayMs) || baseDelayMs > MaxRetryDelay.TotalMilliseconds)
            {
                baseDelayMs = MaxRetryDelay.TotalMilliseconds;
            }

            var baseDelay = TimeSpan.FromMilliseconds(baseDelayMs);

            // Add jitter if enabled (up to 20% of computed delay)
            if (AddJitter)
            {
                var jitterMs = GetJitterMilliseconds(baseDelay);
                baseDelay = baseDelay.Add(TimeSpan.FromMilliseconds(jitterMs));
            }

            // Clamp to MaxRetryDelay
            var clampedDelay = baseDelay > MaxRetryDelay ? MaxRetryDelay : baseDelay;

            return now.Add(clampedDelay);
        }
        catch (OverflowException)
        {
            // In case of overflow, just use MaxRetryDelay
            return now.Add(MaxRetryDelay);
        }
    }

    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Jitter is used for backoff timing and does not require cryptographic randomness."
    )]
    private static double GetJitterMilliseconds(TimeSpan clampedDelay) =>
        clampedDelay.TotalMilliseconds * 0.2 * Random.Shared.NextDouble();
}
