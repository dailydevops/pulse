namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// <see cref="IOutboxOperationsExecutor"/> implementation for the Oracle MySQL EF Core provider
/// (<c>MySql.EntityFrameworkCore</c>).
/// </summary>
/// <remarks>
/// The Oracle MySQL provider has two known limitations:
/// <list type="bullet">
///   <item>
///     It does not apply value converters (e.g. <see cref="DateTimeOffset"/> → <c>bigint</c>)
///     when building <c>ExecuteUpdateAsync</c> parameters, causing a
///     <see cref="NullReferenceException"/> in <c>TypeMappedRelationalParameter.AddDbParameter</c>.
///   </item>
///   <item>
///     It cannot assign a SQL type mapping to a parameterised <see cref="Guid"/> collection used
///     in a <c>WHERE id IN (@ids)</c> clause, causing an <see cref="InvalidOperationException"/>.
///   </item>
/// </list>
/// All mutations go through change tracking and <c>SaveChangesAsync</c> (inherited from
/// <see cref="TrackingOutboxOperationsExecutorBase{TContext}"/>). Only
/// <see cref="UpdateByIdsAsync"/> is overridden: it uses <c>FindAsync</c> per ID to avoid the
/// broken <c>Guid IN</c> clause, while still benefiting from the <see cref="DbContext"/> Local
/// cache for entities already loaded by <c>FetchAndMarkAsync</c>.
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class MySqlOutboxOperationsExecutor<TContext>(TContext context)
    : TrackingOutboxOperationsExecutorBase<TContext>(context)
    where TContext : DbContext, IOutboxDbContext
{
    /// <inheritdoc />
    public override async Task UpdateByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset updatedAt,
        DateTimeOffset? processedAt,
        DateTimeOffset? nextRetryAt,
        OutboxMessageStatus newStatus,
        int retryIncrement,
        string? errorMessage,
        CancellationToken cancellationToken
    )
    {
        // bulkQuery contains a Guid IN clause that MySQL cannot type-map.
        // Use FindAsync per ID instead: it resolves the column's own type mapping and
        // checks the DbContext Local cache before hitting the database.
        var entities = new List<OutboxMessage>(ids.Count);

        foreach (var id in ids)
        {
            var entity = await _context.OutboxMessages.FindAsync([id], cancellationToken).ConfigureAwait(false);

            if (entity?.Status == OutboxMessageStatus.Processing)
            {
                entities.Add(entity);
            }
        }

        if (entities.Count == 0)
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
}
