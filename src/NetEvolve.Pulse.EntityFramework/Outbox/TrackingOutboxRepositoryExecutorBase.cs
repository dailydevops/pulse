namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Base class for <see cref="IOutboxRepositoryExecutor"/> implementations that persist changes
/// through EF Core change tracking and <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// Provides shared implementations of <see cref="FetchAndMarkAsync"/>,
/// <see cref="UpdateByQueryAsync"/>, and <see cref="DeleteByQueryAsync"/> that load entities
/// into the change tracker, apply in-memory mutations, and flush via <c>SaveChangesAsync</c>.
/// Derived classes only need to implement <see cref="UpdateByIdsAsync"/>, which varies by provider.
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal abstract class TrackingOutboxRepositoryExecutorBase<TContext>(TContext context, int maxDegreeOfParallelism)
    : IOutboxRepositoryExecutor
    where TContext : DbContext, IOutboxDbContext
{
    /// <summary>The DbContext used for all tracking-based query and update operations.</summary>
    protected readonly TContext _context = context;

    protected readonly SemaphoreSlim _semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
    private bool _disposedValue;

    /// <inheritdoc />
    public async Task<OutboxMessage[]> FetchAndMarkAsync(
        IQueryable<OutboxMessage> baseQuery,
        DateTimeOffset updatedAt,
        OutboxMessageStatus newStatus,
        CancellationToken cancellationToken
    )
    {
        var entities = await baseQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);

        if (entities.Length == 0)
        {
            return entities;
        }

        foreach (var entity in entities)
        {
            entity.Status = newStatus;
            entity.UpdatedAt = updatedAt;
        }

        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entities;
    }

    /// <inheritdoc />
    public async Task UpdateByQueryAsync(
        IQueryable<OutboxMessage> query,
        DateTimeOffset updatedAt,
        DateTimeOffset? processedAt,
        DateTimeOffset? nextRetryAt,
        OutboxMessageStatus newStatus,
        int retryIncrement,
        string? errorMessage,
        CancellationToken cancellationToken
    )
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entities = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);

            if (entities.Length == 0)
            {
                return;
            }

            foreach (var entity in entities)
            {
                entity.Status = newStatus;
                entity.UpdatedAt = updatedAt;
                if (processedAt.HasValue)
                {
                    entity.ProcessedAt = processedAt.Value;
                }
                entity.NextRetryAt = nextRetryAt;
                entity.RetryCount += retryIncrement;
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    entity.Error = errorMessage;
                }
            }

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public abstract Task UpdateByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset updatedAt,
        DateTimeOffset? processedAt,
        DateTimeOffset? nextRetryAt,
        OutboxMessageStatus newStatus,
        int retryIncrement,
        string? errorMessage,
        CancellationToken cancellationToken
    );

    /// <inheritdoc />
    public async Task<int> DeleteByQueryAsync(IQueryable<OutboxMessage> query, CancellationToken cancellationToken)
    {
        var entities = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);

        if (entities.Length == 0)
        {
            return 0;
        }

        _context.OutboxMessages.RemoveRange(entities);
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entities.Length;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _semaphore.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
