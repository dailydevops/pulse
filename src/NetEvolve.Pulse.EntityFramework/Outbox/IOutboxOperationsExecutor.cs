namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Defines the provider-specific strategy for executing outbox entity operations.
/// </summary>
internal interface IOutboxOperationsExecutor : IDisposable
{
    Task<OutboxMessage[]> FetchAndMarkAsync(
        IQueryable<OutboxMessage> baseQuery,
        DateTimeOffset updatedAt,
        OutboxMessageStatus newStatus,
        CancellationToken cancellationToken
    );

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
