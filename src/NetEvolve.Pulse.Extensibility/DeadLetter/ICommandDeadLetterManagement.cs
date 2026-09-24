namespace NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Defines the contract for managing commands stored in the dead letter store.
/// </summary>
public interface ICommandDeadLetterManagement
{
    /// <summary>
    /// Retrieves the pending dead letter entries, i.e. entries with <see cref="CommandDeadLetterStatus.New"/> status.
    /// </summary>
    /// <param name="count">The maximum number of entries to return. Must be greater than zero. Default: <c>50</c>.</param>
    /// <param name="skip">The number of entries to skip before returning results. Must not be negative. Default: <c>0</c>.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A read-only list of at most <paramref name="count"/> entries with <see cref="CommandDeadLetterStatus.New"/> status,
    /// ordered by <see cref="CommandDeadLetterEntry.OccurredAt"/> ascending (oldest first), after skipping the first
    /// <paramref name="skip"/> entries.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="skip"/> is negative.
    /// </exception>
    Task<IReadOnlyList<CommandDeadLetterEntry>> GetPendingAsync(
        int count = 50,
        int skip = 0,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retrieves the dead letter entry identified by <paramref name="id"/>, regardless of its status.
    /// </summary>
    /// <param name="id">The identifier of the dead letter entry to retrieve.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The matching <see cref="CommandDeadLetterEntry"/>, or <see langword="null"/> if no entry exists.</returns>
    Task<CommandDeadLetterEntry?> GetEntryAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays the command stored in the dead letter entry identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The identifier of the dead letter entry to replay.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations set <see cref="CommandDeadLetterEntry.Status"/> to <see cref="CommandDeadLetterStatus.Replaying"/>,
    /// deserialize the stored payload, dispatch it via <see cref="IMediatorSendOnly"/>, and set
    /// <see cref="CommandDeadLetterEntry.Status"/> to <see cref="CommandDeadLetterStatus.Resolved"/> on success.
    /// Implementations should use the shared <see cref="CommandDeadLetterReplayDispatcher"/> to perform the
    /// type resolution and dispatch, rather than reimplementing the reflection dispatch.
    /// </remarks>
    Task ReplayAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismisses the dead letter entry identified by <paramref name="id"/>, preventing further replay attempts.
    /// </summary>
    /// <param name="id">The identifier of the dead letter entry to dismiss.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations set <see cref="CommandDeadLetterEntry.Status"/> to <see cref="CommandDeadLetterStatus.Dismissed"/>.
    /// </remarks>
    Task DismissAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves aggregate counts of dead letter entries per <see cref="CommandDeadLetterStatus"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="CommandDeadLetterStatistics"/> instance describing the current aggregate counts.</returns>
    Task<CommandDeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
