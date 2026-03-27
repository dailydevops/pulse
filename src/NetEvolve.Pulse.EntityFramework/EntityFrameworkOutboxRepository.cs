namespace NetEvolve.Pulse;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Entity Framework Core implementation of <see cref="IOutboxRepository"/>.
/// Provides CRUD operations for outbox messages using any EF Core database provider.
/// </summary>
/// <remarks>
/// <para><strong>Provider Agnostic:</strong></para>
/// Works with any EF Core database provider (SQL Server, PostgreSQL, SQLite, etc.).
/// <para><strong>Transaction Support:</strong></para>
/// Operations participate in the ambient <see cref="DbContext"/> transaction when one is active.
/// <para><strong>Concurrency:</strong></para>
/// Uses optimistic concurrency with status checks. For high-throughput scenarios,
/// consider using the SQL Server ADO.NET provider with explicit locking.
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class EntityFrameworkOutboxRepository<TContext> : IOutboxRepository
    where TContext : DbContext, IOutboxDbContext
{
    /// <summary>The DbContext used for all LINQ-to-SQL query and update operations.</summary>
    private readonly TContext _context;

    /// <summary>The time provider used to generate consistent update and cutoff timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkOutboxRepository{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public EntityFrameworkOutboxRepository(TContext context, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _ = await _context.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        var ids = await _context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Length == 0)
        {
            return [];
        }

        _ = await _context
            .OutboxMessages.Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(
                m => m.SetProperty(m => m.Status, OutboxMessageStatus.Processing).SetProperty(m => m.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);

        return await _context
            .OutboxMessages.Where(m => ids.Contains(m.Id))
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        var ids = await _context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Failed && m.RetryCount < maxRetryCount)
            .OrderBy(m => m.UpdatedAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Length == 0)
        {
            return [];
        }

        _ = await _context
            .OutboxMessages.Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(
                m => m.SetProperty(m => m.Status, OutboxMessageStatus.Processing).SetProperty(m => m.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);

        return await _context
            .OutboxMessages.Where(m => ids.Contains(m.Id))
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        _ = await _context
            .OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(m => m.Status, OutboxMessageStatus.Completed)
                        .SetProperty(m => m.ProcessedAt, now)
                        .SetProperty(m => m.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken = default
    )
    {
        if (messageIds is null || messageIds.Count == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();

        _ = await _context
            .OutboxMessages.Where(m => messageIds.Contains(m.Id) && m.Status == OutboxMessageStatus.Processing)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(m => m.Status, OutboxMessageStatus.Completed)
                        .SetProperty(m => m.ProcessedAt, now)
                        .SetProperty(m => m.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        _ = await _context
            .OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(m => m.Status, OutboxMessageStatus.Failed)
                        .SetProperty(m => m.Error, errorMessage)
                        .SetProperty(m => m.UpdatedAt, now)
                        .SetProperty(m => m.RetryCount, m => m.RetryCount + 1),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        IReadOnlyCollection<Guid> messageIds,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        if (messageIds is null || messageIds.Count == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();

        _ = await _context
            .OutboxMessages.Where(m => messageIds.Contains(m.Id) && m.Status == OutboxMessageStatus.Processing)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(m => m.Status, OutboxMessageStatus.Failed)
                        .SetProperty(m => m.Error, errorMessage)
                        .SetProperty(m => m.UpdatedAt, now)
                        .SetProperty(m => m.RetryCount, m => m.RetryCount + 1),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsDeadLetterAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        _ = await _context
            .OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(m => m.Status, OutboxMessageStatus.DeadLetter)
                        .SetProperty(m => m.Error, errorMessage)
                        .SetProperty(m => m.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsDeadLetterAsync(
        IReadOnlyCollection<Guid> messageIds,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        if (messageIds is null || messageIds.Count == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();

        _ = await _context
            .OutboxMessages.Where(m => messageIds.Contains(m.Id) && m.Status == OutboxMessageStatus.Processing)
            .ExecuteUpdateAsync(
                m =>
                    m.SetProperty(m => m.Status, OutboxMessageStatus.DeadLetter)
                        .SetProperty(m => m.Error, errorMessage)
                        .SetProperty(m => m.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoffTime = _timeProvider.GetUtcNow().Subtract(olderThan);

        return await _context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Completed && m.ProcessedAt < cutoffTime)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
