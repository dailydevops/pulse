namespace NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Defines the contract for outbox management operations including dead-letter inspection,
/// message replay, and statistics queries.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Provides a programmatic API for platform engineers to inspect dead-letter messages,
/// replay failed deliveries, and query outbox health statistics — without requiring
/// direct database access.
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
    /// <param name="page">Zero-based page index.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of dead-letter messages ordered by <see cref="OutboxMessage.UpdatedAt"/> descending.</returns>
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
    /// Returns a snapshot of outbox message counts grouped by <see cref="OutboxMessageStatus"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An <see cref="OutboxStatistics"/> instance with counts for each status.</returns>
    Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
