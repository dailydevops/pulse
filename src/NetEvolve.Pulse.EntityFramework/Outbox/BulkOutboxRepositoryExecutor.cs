namespace NetEvolve.Pulse.Outbox;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// <see cref="IOutboxRepositoryExecutor"/> implementation that issues a single bulk
/// <c>ExecuteUpdateAsync</c> / <c>ExecuteDeleteAsync</c> statement per operation.
/// </summary>
/// <remarks>
/// Suitable for any EF Core provider that supports these operations and can correctly
/// translate a parameterised <see cref="Guid"/> collection into a SQL <c>IN</c> clause
/// (SQL Server, PostgreSQL, SQLite, and others).
/// A <c>maxDegreeOfParallelism</c> below one is clamped to one, so the executor stays usable
/// on single-CPU hosts where callers derive the value from <see cref="Environment.ProcessorCount"/>.
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class BulkOutboxRepositoryExecutor<TContext>(TContext context, int maxDegreeOfParallelism)
    : IOutboxRepositoryExecutor
    where TContext : DbContext, IOutboxDbContext
{
    private readonly SemaphoreSlim _semaphore = new(
        Math.Max(1, maxDegreeOfParallelism),
        Math.Max(1, maxDegreeOfParallelism)
    );
    private bool _disposedValue;

    /// <inheritdoc />
    public async Task<OutboxMessage[]> FetchAndMarkAsync(
        IQueryable<OutboxMessage> baseQuery,
        Expression<Func<OutboxMessage, bool>> claimFilter,
        DateTimeOffset updatedAt,
        OutboxMessageStatus newStatus,
        CancellationToken cancellationToken
    )
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entities = await baseQuery.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);

            if (entities.Length == 0)
            {
                return [];
            }

            var ids = Array.ConvertAll(entities, m => m.Id);

            // Claim through the plain table with the full eligibility predicate instead of the ordered,
            // limited baseQuery: EF Core would join the target back to a limited subquery, leaving the
            // predicate off the updated row. PostgreSQL READ COMMITTED re-evaluates only the target
            // row's WHERE clause after waiting on a competing claim, so the predicate must live there.
            var claimed = await context
                .OutboxMessages.Where(claimFilter)
                .Where(m => ids.Contains(m.Id))
                .ExecuteUpdateAsync(
                    m => m.SetProperty(m => m.Status, newStatus).SetProperty(m => m.UpdatedAt, updatedAt),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (claimed == entities.Length)
            {
                // Uncontended fast path: the claim re-checks the eligibility predicate on every
                // target row, so the affected-row count only includes rows this caller transitioned.
                // All selected rows were claimed, and the loaded entities can be patched in memory
                // instead of paying a third database round trip for a re-fetch.
                foreach (var entity in entities)
                {
                    entity.Status = newStatus;
                    entity.UpdatedAt = updatedAt;
                }

                return entities;
            }

            if (claimed == 0)
            {
                return [];
            }

            // A competing poller claimed part of the candidate set; re-fetch only the rows
            // this caller actually transitioned, identified by the new status and this
            // caller's UpdatedAt stamp.
            // Known limit: UpdatedAt doubles as the claim token, so two claimers writing an identical
            // stamp (e.g. a shared fake clock) cannot be told apart; see
            // decisions/2026-09-28-entityframework-outbox-claim-concurrency.md.
            var claimedIds = await context
                .OutboxMessages.AsNoTracking()
                .Where(m => ids.Contains(m.Id) && m.Status == newStatus && m.UpdatedAt == updatedAt)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Keep the batch order of the candidate query.
            var result = Array.FindAll(entities, e => claimedIds.Contains(e.Id));
            foreach (var entity in result)
            {
                entity.Status = newStatus;
                entity.UpdatedAt = updatedAt;
            }

            return result;
        }
        finally
        {
            _ = _semaphore.Release();
        }
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
            _ = await query
                .ExecuteUpdateAsync(
                    m =>
                        m.SetProperty(p => p.UpdatedAt, updatedAt)
                            .SetProperty(
                                p => p.ProcessedAt,
                                p =>
#pragma warning disable IDE0030, RCS1084 // Use coalesce expression instead of conditional expression
                                    processedAt.HasValue ? processedAt.Value : p.ProcessedAt
#pragma warning restore IDE0030, RCS1084 // Use coalesce expression instead of conditional expression
                            )
                            .SetProperty(p => p.NextRetryAt, nextRetryAt)
                            .SetProperty(p => p.Status, newStatus)
                            .SetProperty(p => p.RetryCount, p => p.RetryCount + retryIncrement)
                            .SetProperty(p => p.Error, errorMessage),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            _ = _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public Task UpdateByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset updatedAt,
        DateTimeOffset? processedAt,
        DateTimeOffset? nextRetryAt,
        OutboxMessageStatus newStatus,
        int retryIncrement,
        string? errorMessage,
        CancellationToken cancellationToken
    ) =>
        UpdateByQueryAsync(
            context.OutboxMessages.Where(m => ids.Contains(m.Id)),
            updatedAt,
            processedAt,
            nextRetryAt,
            newStatus,
            retryIncrement,
            errorMessage,
            cancellationToken
        );

    /// <inheritdoc />
    public async Task<int> DeleteByQueryAsync(IQueryable<OutboxMessage> query, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await query.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _semaphore.Release();
        }
    }

    private void Dispose(bool disposing)
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
