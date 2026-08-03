namespace NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Represents the aggregate counts of command dead letter entries per <see cref="CommandDeadLetterStatus"/>.
/// This is the value returned by <see cref="ICommandDeadLetterManagement.GetStatisticsAsync"/>.
/// </summary>
/// <param name="NewCount">The number of entries with <see cref="CommandDeadLetterStatus.New"/> status.</param>
/// <param name="ReplayingCount">The number of entries with <see cref="CommandDeadLetterStatus.Replaying"/> status.</param>
/// <param name="ResolvedCount">The number of entries with <see cref="CommandDeadLetterStatus.Resolved"/> status.</param>
/// <param name="DismissedCount">The number of entries with <see cref="CommandDeadLetterStatus.Dismissed"/> status.</param>
public sealed record CommandDeadLetterStatistics(
    int NewCount,
    int ReplayingCount,
    int ResolvedCount,
    int DismissedCount
)
{
    /// <summary>
    /// Gets the total number of dead letter entries across all statuses.
    /// </summary>
    public int TotalCount => NewCount + ReplayingCount + ResolvedCount + DismissedCount;
}
