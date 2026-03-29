namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Per-event-type configuration overrides for the outbox background processor.
/// </summary>
/// <remarks>
/// Any non-<c>null</c> property set here takes precedence over the corresponding global
/// <see cref="OutboxProcessorOptions"/> value when processing a message whose
/// <see cref="OutboxMessage.EventType"/> matches the dictionary key in
/// <see cref="OutboxProcessorOptions.EventTypeOverrides"/>.
/// Properties left as <c>null</c> fall back to the global default.
/// </remarks>
public sealed class OutboxEventTypeOptions
{
    /// <summary>
    /// Gets or sets the maximum number of messages to process in a single batch.
    /// When <c>null</c>, the global <see cref="OutboxProcessorOptions.BatchSize"/> is used.
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// Gets or sets the delay between processing cycles when no messages are found.
    /// When <c>null</c>, the global <see cref="OutboxProcessorOptions.PollingInterval"/> is used.
    /// </summary>
    public TimeSpan? PollingInterval { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts before moving to dead letter.
    /// When <c>null</c>, the global <see cref="OutboxProcessorOptions.MaxRetryCount"/> is used.
    /// </summary>
    public int? MaxRetryCount { get; set; }

    /// <summary>
    /// Gets or sets the timeout for processing a single message.
    /// When <c>null</c>, the global <see cref="OutboxProcessorOptions.ProcessingTimeout"/> is used.
    /// </summary>
    public TimeSpan? ProcessingTimeout { get; set; }

    /// <summary>
    /// Gets or sets whether to enable batch sending via <see cref="IMessageTransport.SendBatchAsync"/>.
    /// When <c>null</c>, the global <see cref="OutboxProcessorOptions.EnableBatchSending"/> is used.
    /// </summary>
    public bool? EnableBatchSending { get; set; }
}
