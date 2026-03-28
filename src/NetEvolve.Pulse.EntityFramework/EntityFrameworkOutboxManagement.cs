namespace NetEvolve.Pulse;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Entity Framework Core implementation of <see cref="IOutboxManagement"/>.
/// Provides dead-letter inspection, replay, and statistics queries using any EF Core database provider.
/// </summary>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class EntityFrameworkOutboxManagement<TContext> : IOutboxManagement
    where TContext : DbContext, IOutboxDbContext
{
    /// <summary>The DbContext used for all LINQ-to-SQL query and update operations.</summary>
    private readonly TContext _context;

    /// <summary>The time provider used to generate consistent update timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkOutboxManagement{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public EntityFrameworkOutboxManagement(TContext context, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetDeadLetterMessagesAsync(
        int pageSize = 50,
        int page = 0,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(page);

        return await _context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.DeadLetter)
            .OrderByDescending(m => m.UpdatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxMessage?> GetDeadLetterMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default
    ) =>
        await _context
            .OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.DeadLetter)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default) =>
        await _context
            .OutboxMessages.LongCountAsync(m => m.Status == OutboxMessageStatus.DeadLetter, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> ReplayMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        var updated = await _context
            .OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.DeadLetter)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                        .SetProperty(m => m.RetryCount, 0)
                        .SetProperty(m => m.Error, (string?)null)
                        .SetProperty(m => m.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);

        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<int> ReplayAllDeadLetterAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        return await _context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.DeadLetter)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                        .SetProperty(m => m.RetryCount, 0)
                        .SetProperty(m => m.Error, (string?)null)
                        .SetProperty(m => m.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _context
            .OutboxMessages.GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = (long)g.Count() })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = 0L;
        var processing = 0L;
        var completed = 0L;
        var failed = 0L;
        var deadLetter = 0L;

        foreach (var entry in counts)
        {
            switch (entry.Status)
            {
                case OutboxMessageStatus.Pending:
                    pending = entry.Count;
                    break;
                case OutboxMessageStatus.Processing:
                    processing = entry.Count;
                    break;
                case OutboxMessageStatus.Completed:
                    completed = entry.Count;
                    break;
                case OutboxMessageStatus.Failed:
                    failed = entry.Count;
                    break;
                case OutboxMessageStatus.DeadLetter:
                    deadLetter = entry.Count;
                    break;
            }
        }

        return new OutboxStatistics
        {
            Pending = pending,
            Processing = processing,
            Completed = completed,
            Failed = failed,
            DeadLetter = deadLetter,
        };
    }
}
