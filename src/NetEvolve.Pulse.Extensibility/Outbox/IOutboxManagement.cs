namespace NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Defines the contract for outbox management operations including message and dead-letter inspection,
/// message replay, dead-letter dismissal, and statistics queries.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Provides a programmatic API for platform engineers to inspect outbox messages in any status
/// (<see cref="GetMessagesAsync"/>, <see cref="GetMessageAsync"/>), inspect dead-letter messages,
/// replay failed deliveries, dismiss dead letters (<see cref="DismissMessageAsync"/>), and query
/// outbox health statistics — without requiring direct database access.
/// <para><strong>Dead-Letter Messages:</strong></para>
/// Messages move to dead-letter state after exceeding the configured maximum retry count.
/// Use <see cref="GetDeadLetterMessagesAsync"/> to inspect them and <see cref="ReplayMessageAsync"/>
/// or <see cref="ReplayAllDeadLetterAsync"/> to re-queue them for processing.
/// <para><strong>Thread Safety:</strong></para>
/// Implementations SHOULD be thread-safe for concurrent administrative access.
/// </remarks>
public interface IOutboxManagement
{
    /// <summary>
    /// Returns a paginated list of dead-letter messages.
    /// </summary>
    /// <param name="pageSize">Maximum number of messages to return per page. Must be greater than zero.</param>
    /// <param name="page">Zero-based page index. Must not be negative.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of dead-letter messages ordered by <see cref="OutboxMessage.UpdatedAt"/> descending.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="pageSize"/> is not positive, <paramref name="page"/> is negative,
    /// or the resulting offset (<paramref name="page"/> × <paramref name="pageSize"/>) exceeds <see cref="int.MaxValue"/>.
    /// </exception>
    Task<IReadOnlyList<OutboxMessage>> GetDeadLetterMessagesAsync(
        int pageSize = 50,
        int page = 0,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the dead-letter message with the specified identifier, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="messageId">The unique identifier of the dead-letter message.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The matching <see cref="OutboxMessage"/>, or <see langword="null"/>.</returns>
    Task<OutboxMessage?> GetDeadLetterMessageAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of messages currently in dead-letter state.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The dead-letter message count.</returns>
    Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets a single dead-letter message back to <see cref="OutboxMessageStatus.Pending"/> so
    /// the outbox processor will attempt delivery again.
    /// </summary>
    /// <param name="messageId">The unique identifier of the dead-letter message to replay.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> if the message was found in dead-letter state and reset;
    /// <see langword="false"/> if no matching dead-letter message exists.
    /// </returns>
    /// <remarks>
    /// Replaying resets <see cref="OutboxMessage.RetryCount"/> to zero and clears
    /// <see cref="OutboxMessage.Error"/> so the message gets a fresh set of delivery attempts.
    /// </remarks>
    Task<bool> ReplayMessageAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets all dead-letter messages back to <see cref="OutboxMessageStatus.Pending"/> so
    /// the outbox processor will attempt delivery again.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The number of messages that were reset.</returns>
    /// <remarks>
    /// Replaying resets <see cref="OutboxMessage.RetryCount"/> to zero and clears
    /// <see cref="OutboxMessage.Error"/> for every affected message.
    /// </remarks>
    Task<int> ReplayAllDeadLetterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paginated, read-only list of outbox messages in any status, optionally filtered by status.
    /// </summary>
    /// <param name="pageSize">Maximum number of messages to return per page. Must be greater than zero.</param>
    /// <param name="page">Zero-based page index. Must not be negative.</param>
    /// <param name="status">
    /// When set, only messages in this <see cref="OutboxMessageStatus"/> are returned;
    /// when <see langword="null"/>, messages in every status are returned.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A read-only list of messages ordered by <see cref="OutboxMessage.UpdatedAt"/> descending.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="pageSize"/> is not positive, <paramref name="page"/> is negative,
    /// or the resulting offset (<paramref name="page"/> × <paramref name="pageSize"/>) exceeds <see cref="int.MaxValue"/>.
    /// </exception>
    /// <remarks>
    /// Unlike <see cref="IOutboxRepository.GetPendingAsync"/>, this method never changes the
    /// status of the returned messages, so it is safe to call while the outbox processor is running.
    /// </remarks>
    Task<IReadOnlyList<OutboxMessage>> GetMessagesAsync(
        int pageSize = 50,
        int page = 0,
        OutboxMessageStatus? status = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the outbox message with the specified identifier in any status, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="messageId">The unique identifier of the outbox message.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The matching <see cref="OutboxMessage"/>, or <see langword="null"/>.</returns>
    Task<OutboxMessage?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismisses a dead-letter message by permanently deleting it from the outbox, so it is
    /// neither replayed nor reported as a dead letter anymore.
    /// </summary>
    /// <param name="messageId">The unique identifier of the dead-letter message to dismiss.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> if the message was found in dead-letter state and deleted;
    /// <see langword="false"/> if no matching dead-letter message exists.
    /// </returns>
    /// <remarks>
    /// Only messages in <see cref="OutboxMessageStatus.DeadLetter"/> state are affected; messages in any
    /// other status are left untouched. Dismissing cannot be undone.
    /// </remarks>
    Task<bool> DismissMessageAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a snapshot of outbox message counts grouped by <see cref="OutboxMessageStatus"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An <see cref="OutboxStatistics"/> instance with counts for each status.</returns>
    Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
