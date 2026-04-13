namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// <see cref="IOutboxOperationsExecutor"/> implementation that issues a single bulk
/// <c>ExecuteUpdateAsync</c> / <c>ExecuteDeleteAsync</c> statement per operation.
/// </summary>
/// <remarks>
/// Suitable for any EF Core provider that supports these operations and can correctly
/// translate a parameterised <see cref="Guid"/> collection into a SQL <c>IN</c> clause
/// (SQL Server, PostgreSQL, SQLite, and others).
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class BulkOutboxOperationsExecutor<TContext>(TContext context) : IOutboxOperationsExecutor
    where TContext : DbContext, IOutboxDbContext
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposedValue;

    /// <inheritdoc />
    public async Task<OutboxMessage[]> FetchAndMarkAsync(
        IQueryable<OutboxMessage> baseQuery,
        DateTimeOffset updatedAt,
        OutboxMessageStatus newStatus,
        CancellationToken cancellationToken
    )
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ids = await baseQuery
                .AsNoTracking()
                .Select(m => m.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ids.Length == 0)
            {
                return [];
            }

            _ = await baseQuery
                .Where(m => ids.Contains(m.Id))
                .ExecuteUpdateAsync(
                    m => m.SetProperty(m => m.Status, newStatus).SetProperty(m => m.UpdatedAt, updatedAt),
                    cancellationToken
                )
                .ConfigureAwait(false);

            return await context
                .OutboxMessages.AsNoTracking()
                .Where(m => ids.Contains(m.Id))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
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
