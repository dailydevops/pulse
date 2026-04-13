namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Defines the provider-specific strategy for executing outbox entity operations.
/// </summary>
internal interface IOutboxRepositoryExecutor : IDisposable
{
    /// <summary>
    /// Atomically fetches the entities returned by <paramref name="baseQuery"/>, marks each one
    /// with <paramref name="newStatus"/> and <paramref name="updatedAt"/>, and returns the
    /// updated messages.
    /// </summary>
    /// <remarks>
    /// This is the core "fetch-and-lock" primitive used by the outbox processor.
    /// Marking messages as <see cref="OutboxMessageStatus.Processing"/> before handing them to
    /// the caller prevents concurrent workers from picking up the same batch.
    /// </remarks>
    /// <param name="baseQuery">
    /// A pre-filtered, pre-ordered, and already-limited query that selects the candidate messages.
    /// </param>
    /// <param name="updatedAt">
    /// The timestamp written to <see cref="OutboxMessage.UpdatedAt"/> for every updated message.
    /// </param>
    /// <param name="newStatus">
    /// The status to assign to each message, typically <see cref="OutboxMessageStatus.Processing"/>.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The messages that were fetched and marked, in the order returned by the database.</returns>
    Task<OutboxMessage[]> FetchAndMarkAsync(
        IQueryable<OutboxMessage> baseQuery,
        DateTimeOffset updatedAt,
        OutboxMessageStatus newStatus,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Applies a status transition and related field updates to every message matched by
    /// <paramref name="query"/>.
    /// </summary>
    /// <remarks>
    /// Used to record the outcome of a single message (completed, failed, or dead-lettered).
    /// <paramref name="processedAt"/> is only written when it carries a value; it is left
    /// unchanged otherwise. <paramref name="errorMessage"/> replaces any previously stored error.
    /// <paramref name="retryIncrement"/> is added to the current <see cref="OutboxMessage.RetryCount"/>.
    /// </remarks>
    /// <param name="query">
    /// A pre-filtered query that targets only the rows to update, typically filtered by
    /// message ID and <see cref="OutboxMessageStatus.Processing"/>.
    /// </param>
    /// <param name="updatedAt">Timestamp written to <see cref="OutboxMessage.UpdatedAt"/>.</param>
    /// <param name="processedAt">
    /// When not <see langword="null"/>, written to <see cref="OutboxMessage.ProcessedAt"/>;
    /// the existing value is preserved when <see langword="null"/>.
    /// </param>
    /// <param name="nextRetryAt">
    /// The earliest time the message may be retried; written to <see cref="OutboxMessage.NextRetryAt"/>.
    /// Pass <see langword="null"/> to clear a previously scheduled retry.
    /// </param>
    /// <param name="newStatus">The target <see cref="OutboxMessageStatus"/> for the message.</param>
    /// <param name="retryIncrement">
    /// The non-negative value added to <see cref="OutboxMessage.RetryCount"/>. Pass <c>0</c> when
    /// the retry counter should not change (e.g. on completion or dead-lettering).
    /// </param>
    /// <param name="errorMessage">
    /// The error text to record in <see cref="OutboxMessage.Error"/>;
    /// pass <see langword="null"/> to clear any previous error.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task UpdateByQueryAsync(
        IQueryable<OutboxMessage> query,
        DateTimeOffset updatedAt,
        DateTimeOffset? processedAt,
        DateTimeOffset? nextRetryAt,
        OutboxMessageStatus newStatus,
        int retryIncrement,
        string? errorMessage,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Applies a status transition and related field updates to a batch of messages identified
    /// by their <paramref name="ids"/>.
    /// </summary>
    /// <remarks>
    /// Semantically equivalent to <see cref="UpdateByQueryAsync"/> but targets messages by ID
    /// rather than by an arbitrary query. Used for bulk outcome reporting when the caller already
    /// holds a collection of message identifiers.
    /// Implementations may issue a single bulk statement or iterate per ID, depending on what the
    /// underlying EF Core provider supports.
    /// </remarks>
    /// <param name="ids">The identifiers of the messages to update.</param>
    /// <param name="updatedAt">Timestamp written to <see cref="OutboxMessage.UpdatedAt"/>.</param>
    /// <param name="processedAt">
    /// When not <see langword="null"/>, written to <see cref="OutboxMessage.ProcessedAt"/>;
    /// the existing value is preserved when <see langword="null"/>.
    /// </param>
    /// <param name="nextRetryAt">
    /// The earliest time the messages may be retried; written to <see cref="OutboxMessage.NextRetryAt"/>.
    /// Pass <see langword="null"/> to clear a previously scheduled retry.
    /// </param>
    /// <param name="newStatus">The target <see cref="OutboxMessageStatus"/> for each message.</param>
    /// <param name="retryIncrement">
    /// The non-negative value added to <see cref="OutboxMessage.RetryCount"/>. Pass <c>0</c> when
    /// the retry counter should not change.
    /// </param>
    /// <param name="errorMessage">
    /// The error text to record in <see cref="OutboxMessage.Error"/>;
    /// pass <see langword="null"/> to clear any previous error.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task UpdateByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset updatedAt,
        DateTimeOffset? processedAt,
        DateTimeOffset? nextRetryAt,
        OutboxMessageStatus newStatus,
        int retryIncrement,
        string? errorMessage,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Deletes the entities matched by <paramref name="query"/> and returns the number of
    /// rows removed.
    /// </summary>
    /// <param name="query">A pre-filtered query that targets only the rows to delete.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    Task<int> DeleteByQueryAsync(IQueryable<OutboxMessage> query, CancellationToken cancellationToken);
}
