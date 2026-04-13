namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// <see cref="IOutboxManagementExecutor"/> implementation that persists changes through
/// EF Core change tracking and <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// Used for providers that do not support <c>ExecuteUpdateAsync</c> (InMemory) or that
/// have known value-converter limitations when building update parameters (Oracle MySQL).
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class TrackingOutboxManagementExecutor<TContext>(TContext context) : IOutboxManagementExecutor
    where TContext : DbContext, IOutboxDbContext
{
    /// <inheritdoc />
    public IQueryable<OutboxMessage> GetDeadLetterMessages(int skip, int take) =>
        context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.DeadLetter)
            .OrderByDescending(m => m.UpdatedAt)
            .ThenBy(m => m.Id)
            .Skip(skip)
            .Take(take)
            .AsNoTracking();

    /// <inheritdoc />
    public Task<OutboxMessage?> GetDeadLetterMessageAsync(Guid id, CancellationToken cancellationToken) =>
        context
            .OutboxMessages.Where(m => m.Id == id && m.Status == OutboxMessageStatus.DeadLetter)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ReplayByIdAsync(Guid id, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var entity = await context
            .OutboxMessages.FirstOrDefaultAsync(
                m => m.Id == id && m.Status == OutboxMessageStatus.DeadLetter,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        entity.Status = OutboxMessageStatus.Pending;
        entity.RetryCount = 0;
        entity.Error = null;
        entity.UpdatedAt = updatedAt;

        _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task<int> ReplayAllAsync(DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var entities = await context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.DeadLetter)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entities.Length == 0)
        {
            return 0;
        }

        foreach (var entity in entities)
        {
            entity.Status = OutboxMessageStatus.Pending;
            entity.RetryCount = 0;
            entity.Error = null;
            entity.UpdatedAt = updatedAt;
        }

        _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entities.Length;
    }
}
