namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Entity Framework Core implementation of <see cref="IOutboxManagement"/>.
/// Provides dead-letter inspection, replay, and statistics queries using any EF Core database provider.
/// </summary>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class EntityFrameworkOutboxManagement<TContext> : IOutboxManagement
    where TContext : DbContext, IOutboxDbContext
{
    /// <summary>Pre-compiled paged dead-letter query; eliminates expression-tree overhead on every call.</summary>
    private static readonly Func<TContext, int, int, IAsyncEnumerable<OutboxMessage>> _deadLetterPageQuery =
        EF.CompileAsyncQuery(
            (TContext ctx, int skip, int take) =>
                ctx
                    .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.DeadLetter)
                    .OrderByDescending(m => m.UpdatedAt)
                    .Skip(skip)
                    .Take(take)
                    .AsNoTracking()
        );

    /// <summary>Pre-compiled single dead-letter lookup; eliminates expression-tree overhead on every call.</summary>
    private static readonly Func<TContext, Guid, Task<OutboxMessage?>> _deadLetterByIdQuery = EF.CompileAsyncQuery(
        (TContext ctx, Guid id) =>
            ctx
                .OutboxMessages.Where(m => m.Id == id && m.Status == OutboxMessageStatus.DeadLetter)
                .AsNoTracking()
                .FirstOrDefault()
    );

    /// <summary>Pre-compiled dead-letter count query; eliminates expression-tree overhead on every call.</summary>
    private static readonly Func<TContext, Task<long>> _deadLetterCountQuery = EF.CompileAsyncQuery(
        (TContext ctx) => ctx.OutboxMessages.LongCount(m => m.Status == OutboxMessageStatus.DeadLetter)
    );

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

        var result = new List<OutboxMessage>(pageSize);
        await foreach (
            var message in _deadLetterPageQuery(_context, page * pageSize, pageSize)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            result.Add(message);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<OutboxMessage?> GetDeadLetterMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _deadLetterByIdQuery(_context, messageId);
    }

    /// <inheritdoc />
    public Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _deadLetterCountQuery(_context);
    }

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
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken)
            .ConfigureAwait(false);

        return new OutboxStatistics
        {
            Pending = counts.GetValueOrDefault(OutboxMessageStatus.Pending),
            Processing = counts.GetValueOrDefault(OutboxMessageStatus.Processing),
            Completed = counts.GetValueOrDefault(OutboxMessageStatus.Completed),
            Failed = counts.GetValueOrDefault(OutboxMessageStatus.Failed),
            DeadLetter = counts.GetValueOrDefault(OutboxMessageStatus.DeadLetter),
        };
    }
}
