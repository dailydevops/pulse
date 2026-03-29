namespace NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Represents a snapshot of outbox message counts grouped by processing status.
/// </summary>
public sealed class OutboxStatistics
{
    /// <summary>
    /// Gets the number of messages waiting to be processed.
    /// </summary>
    public long Pending { get; init; }

    /// <summary>
    /// Gets the number of messages currently being processed.
    /// </summary>
    public long Processing { get; init; }

    /// <summary>
    /// Gets the number of messages that have been successfully delivered.
    /// </summary>
    public long Completed { get; init; }

    /// <summary>
    /// Gets the number of messages that failed and are awaiting retry.
    /// </summary>
    public long Failed { get; init; }

    /// <summary>
    /// Gets the number of messages that have exceeded the maximum retry count
    /// and moved to the dead-letter state.
    /// </summary>
    public long DeadLetter { get; init; }

    /// <summary>
    /// Gets the total number of messages across all statuses.
    /// </summary>
    public long Total => Pending + Processing + Completed + Failed + DeadLetter;
}
