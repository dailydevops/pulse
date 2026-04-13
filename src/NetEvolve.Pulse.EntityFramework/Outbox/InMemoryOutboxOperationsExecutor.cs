namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// <see cref="IOutboxOperationsExecutor"/> implementation for the EF Core InMemory provider.
/// </summary>
/// <remarks>
/// The InMemory provider evaluates all LINQ in process, so any query — including those with
/// <c>Contains</c> over a <see cref="Guid"/> collection — works without type-mapping issues.
/// <c>ExecuteUpdate</c> / <c>ExecuteDelete</c> are not supported; all mutations go through
/// change tracking and <c>SaveChangesAsync</c> (inherited from
/// <see cref="TrackingOutboxOperationsExecutorBase{TContext}"/>).
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class InMemoryOutboxOperationsExecutor<TContext>(TContext context, int maxDegreeOfParallelism)
    : TrackingOutboxOperationsExecutorBase<TContext>(context, maxDegreeOfParallelism)
    where TContext : DbContext, IOutboxDbContext
{
    /// <inheritdoc />
    public override Task UpdateByIdsAsync(
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
            _context.OutboxMessages.Where(m => ids.Contains(m.Id)),
            updatedAt,
            processedAt,
            nextRetryAt,
            newStatus,
            retryIncrement,
            errorMessage,
            cancellationToken
        );
}
