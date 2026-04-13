namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Defines the provider-specific strategy for executing outbox management operations.
/// </summary>
internal interface IOutboxManagementExecutor
{
    /// <summary>
    /// Returns a paged, non-tracking query of dead-letter messages ordered by
    /// <see cref="OutboxMessage.UpdatedAt"/> descending.
    /// </summary>
    /// <param name="skip">The number of messages to skip (offset).</param>
    /// <param name="take">The maximum number of messages to return (page size).</param>
    /// <returns>
    /// An <see cref="IQueryable{T}"/> that can be further composed or materialized
    /// by the caller (e.g. via <c>ToListAsync</c>).
    /// </returns>
    IQueryable<OutboxMessage> GetDeadLetterMessages(int skip, int take);

    /// <summary>
    /// Returns the dead-letter message with the given identifier, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="id">The unique identifier of the dead-letter message to retrieve.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The matching <see cref="OutboxMessage"/>, or <see langword="null"/>.</returns>
    Task<OutboxMessage?> GetDeadLetterMessageAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Resets a single dead-letter message back to <see cref="OutboxMessageStatus.Pending"/> state.
    /// </summary>
    /// <param name="id">The unique identifier of the dead-letter message to replay.</param>
    /// <param name="updatedAt">The timestamp to write into <see cref="OutboxMessage.UpdatedAt"/>.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns><see langword="true"/> if the message was found and reset; otherwise <see langword="false"/>.</returns>
    Task<bool> ReplayByIdAsync(Guid id, DateTimeOffset updatedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Resets all dead-letter messages back to <see cref="OutboxMessageStatus.Pending"/> state.
    /// </summary>
    /// <param name="updatedAt">The timestamp to write into <see cref="OutboxMessage.UpdatedAt"/> for each message.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The number of messages that were reset.</returns>
    Task<int> ReplayAllAsync(DateTimeOffset updatedAt, CancellationToken cancellationToken);
}
