namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents the processing status of an outbox message.
/// </summary>
public enum OutboxMessageStatus
{
    /// <summary>
    /// The message is pending and awaiting processing.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The message is currently being processed.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// The message has been successfully processed.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// The message processing has failed and may be retried.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The message has exceeded retry limits and moved to dead letter.
    /// </summary>
    DeadLetter = 4,
}
