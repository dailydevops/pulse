namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

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
    /// <see langword="true"/> when the current EF Core provider is the in-memory provider,
    /// which does not support <c>ExecuteUpdate</c> / <c>ExecuteDelete</c>.
    /// </summary>
    private readonly bool _useTrackingOperations;

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
        var providerName = context.Database.ProviderName;
        _useTrackingOperations = providerName == "Microsoft.EntityFrameworkCore.InMemory";
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
            .OutboxMessages.AsNoTracking()
            .Where(m => m.Status == OutboxMessageStatus.Pending && (m.NextRetryAt == null || m.NextRetryAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Length == 0)
        {
            return [];
        }

        await UpdateEntitiesAsync(
                _context.OutboxMessages.Where(m => ids.Contains(m.Id) && m.Status == OutboxMessageStatus.Pending),
                msg =>
                {
                    msg.Status = OutboxMessageStatus.Processing;
                    msg.UpdatedAt = now;
                },
                (q, ct) =>
                    q.ExecuteUpdateAsync(
                        m =>
                            m.SetProperty(m => m.Status, OutboxMessageStatus.Processing)
                                .SetProperty(m => m.UpdatedAt, now),
                        ct
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return await _context
            .OutboxMessages.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default) =>
        _context.OutboxMessages.LongCountAsync(m => m.Status == OutboxMessageStatus.Pending, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
        // A simple connectivity check to ensure the database is reachable.
        =>
        _context.Database.CanConnectAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        var ids = await _context
            .OutboxMessages.AsNoTracking()
            .Where(m =>
                m.Status == OutboxMessageStatus.Failed
                && m.RetryCount < maxRetryCount
                && (m.NextRetryAt == null || m.NextRetryAt <= now)
            )
            .OrderBy(m => m.UpdatedAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Length == 0)
        {
            return [];
        }

        await UpdateEntitiesAsync(
                _context.OutboxMessages.Where(m => ids.Contains(m.Id) && m.Status == OutboxMessageStatus.Failed),
                msg =>
                {
                    msg.Status = OutboxMessageStatus.Processing;
                    msg.UpdatedAt = now;
                },
                (q, ct) =>
                    q.ExecuteUpdateAsync(
                        m =>
                            m.SetProperty(m => m.Status, OutboxMessageStatus.Processing)
                                .SetProperty(m => m.UpdatedAt, now),
                        ct
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return await _context
            .OutboxMessages.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        await UpdateEntitiesAsync(
                _context.OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing),
                msg =>
                {
                    msg.Status = OutboxMessageStatus.Completed;
                    msg.ProcessedAt = now;
                    msg.UpdatedAt = now;
                },
                (q, ct) =>
                    q.ExecuteUpdateAsync(
                        m =>
                            m.SetProperty(m => m.Status, OutboxMessageStatus.Completed)
                                .SetProperty(m => m.ProcessedAt, now)
                                .SetProperty(m => m.UpdatedAt, now),
                        ct
                    ),
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

        await UpdateEntitiesAsync(
                _context.OutboxMessages.Where(m =>
                    messageIds.Contains(m.Id) && m.Status == OutboxMessageStatus.Processing
                ),
                msg =>
                {
                    msg.Status = OutboxMessageStatus.Completed;
                    msg.ProcessedAt = now;
                    msg.UpdatedAt = now;
                },
                (q, ct) =>
                    q.ExecuteUpdateAsync(
                        m =>
                            m.SetProperty(m => m.Status, OutboxMessageStatus.Completed)
                                .SetProperty(m => m.ProcessedAt, now)
                                .SetProperty(m => m.UpdatedAt, now),
                        ct
                    ),
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

        await UpdateEntitiesAsync(
                _context.OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing),
                msg =>
                {
                    msg.Status = OutboxMessageStatus.Failed;
                    msg.Error = errorMessage;
                    msg.UpdatedAt = now;
                    msg.RetryCount++;
                },
                (q, ct) =>
                    q.ExecuteUpdateAsync(
                        m =>
                            m.SetProperty(m => m.Status, OutboxMessageStatus.Failed)
                                .SetProperty(m => m.Error, errorMessage)
                                .SetProperty(m => m.UpdatedAt, now)
                                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1),
                        ct
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        DateTimeOffset? nextRetryAt,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        await UpdateEntitiesAsync(
                _context.OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing),
                msg =>
                {
                    msg.Status = OutboxMessageStatus.Failed;
                    msg.Error = errorMessage;
                    msg.UpdatedAt = now;
                    msg.RetryCount++;
                    msg.NextRetryAt = nextRetryAt;
                },
                (q, ct) =>
                    q.ExecuteUpdateAsync(
                        m =>
                            m.SetProperty(m => m.Status, OutboxMessageStatus.Failed)
                                .SetProperty(m => m.Error, errorMessage)
                                .SetProperty(m => m.UpdatedAt, now)
                                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                                .SetProperty(m => m.NextRetryAt, nextRetryAt),
                        ct
                    ),
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

        await UpdateEntitiesAsync(
                _context.OutboxMessages.Where(m =>
                    messageIds.Contains(m.Id) && m.Status == OutboxMessageStatus.Processing
                ),
                msg =>
                {
                    msg.Status = OutboxMessageStatus.Failed;
                    msg.Error = errorMessage;
                    msg.UpdatedAt = now;
                    msg.RetryCount++;
                },
                (q, ct) =>
                    q.ExecuteUpdateAsync(
                        m =>
                            m.SetProperty(m => m.Status, OutboxMessageStatus.Failed)
                                .SetProperty(m => m.Error, errorMessage)
                                .SetProperty(m => m.UpdatedAt, now)
                                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1),
                        ct
                    ),
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

        await UpdateEntitiesAsync(
                _context.OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing),
                msg =>
                {
                    msg.Status = OutboxMessageStatus.DeadLetter;
                    msg.Error = errorMessage;
                    msg.UpdatedAt = now;
                },
                (q, ct) =>
                    q.ExecuteUpdateAsync(
                        m =>
                            m.SetProperty(m => m.Status, OutboxMessageStatus.DeadLetter)
                                .SetProperty(m => m.Error, errorMessage)
                                .SetProperty(m => m.UpdatedAt, now),
                        ct
                    ),
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

        await UpdateEntitiesAsync(
                _context.OutboxMessages.Where(m =>
                    messageIds.Contains(m.Id) && m.Status == OutboxMessageStatus.Processing
                ),
                msg =>
                {
                    msg.Status = OutboxMessageStatus.DeadLetter;
                    msg.Error = errorMessage;
                    msg.UpdatedAt = now;
                },
                (q, ct) =>
                    q.ExecuteUpdateAsync(
                        m =>
                            m.SetProperty(m => m.Status, OutboxMessageStatus.DeadLetter)
                                .SetProperty(m => m.Error, errorMessage)
                                .SetProperty(m => m.UpdatedAt, now),
                        ct
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoffTime = _timeProvider.GetUtcNow().Subtract(olderThan);

        return await DeleteEntitiesAsync(
                _context.OutboxMessages.Where(m =>
                    m.Status == OutboxMessageStatus.Completed && m.ProcessedAt < cutoffTime
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a set of <see cref="OutboxMessage"/> entities by either tracking-and-saving (InMemory provider)
    /// or issuing a single bulk <c>UPDATE</c> statement via <c>ExecuteUpdateAsync</c> (all other providers).
    /// </summary>
    private async Task UpdateEntitiesAsync(
        IQueryable<OutboxMessage> query,
        Action<OutboxMessage> applyChanges,
        Func<IQueryable<OutboxMessage>, CancellationToken, Task<int>> executeBulkUpdate,
        CancellationToken cancellationToken
    )
    {
        if (_useTrackingOperations)
        {
            var entities = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);

            if (entities.Length == 0)
            {
                return;
            }

            foreach (var entity in entities)
            {
                applyChanges(entity);
            }

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _ = await executeBulkUpdate(query, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes a set of <see cref="OutboxMessage"/> entities by either tracking-and-removing (InMemory provider)
    /// or issuing a single bulk <c>DELETE</c> statement via <c>ExecuteDeleteAsync</c> (all other providers).
    /// </summary>
    /// <returns>The number of deleted rows.</returns>
    private async Task<int> DeleteEntitiesAsync(IQueryable<OutboxMessage> query, CancellationToken cancellationToken)
    {
        if (_useTrackingOperations)
        {
            var entities = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);

            if (entities.Length == 0)
            {
                return 0;
            }

            _context.OutboxMessages.RemoveRange(entities);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return entities.Length;
        }

        return await query.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
