namespace NetEvolve.Pulse.Outbox;

using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// Gets per-event-type configuration overrides via a thread-safe concurrent dictionary.
    /// </summary>
    /// <remarks>
    /// <para><strong>Concurrency Guarantees:</strong></para>
    /// The property uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread-safe access.
    /// This is required because the dictionary may be read concurrently during <see cref="OutboxProcessorHostedService.ProcessBatchAsync(CancellationToken)"/>
    /// while being modified externally through code or dependency injection configuration.
    /// <para><strong>Dictionary Key Format:</strong></para>
    /// The dictionary key must match the <see cref="OutboxMessage.EventType"/> value of the messages
    /// to be overridden using an exact, case-sensitive (ordinal) comparison.
    /// <para><strong>Override Precedence:</strong></para>
    /// Any non-<c>null</c> property on the associated <see cref="OutboxEventTypeOptions"/> takes
    /// precedence over the corresponding global default for messages of that event type.
    /// Properties left as <c>null</c> fall back to the global default.
    /// <para><strong>Stored but Unapplied Properties:</strong></para>
    /// Note: <see cref="OutboxEventTypeOptions.BatchSize"/> and
    /// <see cref="OutboxEventTypeOptions.PollingInterval"/> are stored for completeness but are
    /// currently not applied at per-event-type level by the processor, which uses a single polling
    /// cycle for all event types.
    /// </remarks>
    public ConcurrentDictionary<string, OutboxEventTypeOptions> EventTypeOverrides { get; } =
        new ConcurrentDictionary<string, OutboxEventTypeOptions>(StringComparer.Ordinal);

    /// <summary>
    /// Returns the effective <see cref="MaxRetryCount"/> for the given event type,
    /// applying any configured per-type override.
    /// </summary>
    /// <param name="eventType">The event type name.</param>
    /// <returns>The resolved maximum retry count.</returns>
    internal int GetEffectiveMaxRetryCount(string eventType) =>
        EventTypeOverrides.TryGetValue(eventType, out var overrides) && overrides.MaxRetryCount.HasValue
            ? overrides.MaxRetryCount.Value
            : MaxRetryCount;

    /// <summary>
    /// Returns the effective <see cref="ProcessingTimeout"/> for the given event type,
    /// applying any configured per-type override.
    /// </summary>
    /// <param name="eventType">The event type name.</param>
    /// <returns>The resolved processing timeout.</returns>
    internal TimeSpan GetEffectiveProcessingTimeout(string eventType) =>
        EventTypeOverrides.TryGetValue(eventType, out var overrides) && overrides.ProcessingTimeout.HasValue
            ? overrides.ProcessingTimeout.Value
            : ProcessingTimeout;

    /// <summary>
    /// Returns the effective <see cref="EnableBatchSending"/> value for the given event type,
    /// applying any configured per-type override.
    /// </summary>
    /// <param name="eventType">The event type name.</param>
    /// <returns>The resolved batch sending flag.</returns>
    internal bool GetEffectiveEnableBatchSending(string eventType) =>
        EventTypeOverrides.TryGetValue(eventType, out var overrides) && overrides.EnableBatchSending.HasValue
            ? overrides.EnableBatchSending.Value
            : EnableBatchSending;
}
