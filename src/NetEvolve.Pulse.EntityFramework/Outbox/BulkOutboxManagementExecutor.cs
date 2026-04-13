namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// <see cref="IOutboxManagementExecutor"/> implementation that issues bulk
/// <c>ExecuteUpdateAsync</c> statements per operation.
/// </summary>
/// <remarks>
/// Suitable for any EF Core provider that supports <c>ExecuteUpdateAsync</c> and correctly
/// handles value converters in the update parameters (SQL Server, PostgreSQL, SQLite,
/// Pomelo MySQL, and others).
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class BulkOutboxManagementExecutor<TContext>(TContext context) : IOutboxManagementExecutor
    where TContext : DbContext, IOutboxDbContext
{
    /// <inheritdoc />
    public IQueryable<OutboxMessage> GetDeadLetterMessages(int skip, int take) =>
        context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.DeadLetter)
            .OrderByDescending(m => m.UpdatedAt)
            .ThenByDescending(m => m.Id)
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
        var updated = await context
            .OutboxMessages.Where(m => m.Id == id && m.Status == OutboxMessageStatus.DeadLetter)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(p => p.Status, OutboxMessageStatus.Pending)
                        .SetProperty(p => p.RetryCount, 0)
                        .SetProperty(p => p.Error, (string?)null)
                        .SetProperty(p => p.UpdatedAt, updatedAt),
                cancellationToken
            )
            .ConfigureAwait(false);

        return updated > 0;
    }

    /// <inheritdoc />
    public Task<int> ReplayAllAsync(DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
        context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.DeadLetter)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(p => p.Status, OutboxMessageStatus.Pending)
                        .SetProperty(p => p.RetryCount, 0)
                        .SetProperty(p => p.Error, (string?)null)
                        .SetProperty(p => p.UpdatedAt, updatedAt),
                cancellationToken
            );
}
