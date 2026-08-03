namespace NetEvolve.Pulse.Audit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Entity Framework Core implementation of <see cref="IAuditManagement"/>.
/// Provides audit trail querying and statistics using any EF Core database provider.
/// </summary>
/// <remarks>
/// All read operations run directly against <see cref="IAuditStoreDbContext.AuditEntries"/>
/// using plain LINQ queries, and are therefore provider-agnostic.
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IAuditStoreDbContext"/>.</typeparam>
internal sealed class EntityFrameworkAuditManagement<TContext> : IAuditManagement
    where TContext : DbContext, IAuditStoreDbContext
{
    private readonly TContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkAuditManagement{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    public EntityFrameworkAuditManagement(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditRecord>> QueryAsync(
        AuditFilter filter,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _context.AuditEntries.AsQueryable();

        if (filter.CommandType is not null)
        {
            query = query.Where(e => e.CommandType == filter.CommandType);
        }

        if (filter.UserId is not null)
        {
            query = query.Where(e => e.UserId == filter.UserId);
        }

        if (filter.From is not null)
        {
            query = query.Where(e => e.OccurredAt >= filter.From.Value);
        }

        if (filter.To is not null)
        {
            query = query.Where(e => e.OccurredAt <= filter.To.Value);
        }

        if (filter.Result is not null)
        {
            query = query.Where(e => e.Result == filter.Result.Value);
        }

        return await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _context
            .AuditEntries.GroupBy(e => e.Result)
            .Select(g => new { Result = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Result, g => g.Count, cancellationToken)
            .ConfigureAwait(false);

        return new AuditStatistics(
            SuccessCount: counts.GetValueOrDefault(AuditResult.Success),
            FailureCount: counts.GetValueOrDefault(AuditResult.Failure)
        );
    }
}
