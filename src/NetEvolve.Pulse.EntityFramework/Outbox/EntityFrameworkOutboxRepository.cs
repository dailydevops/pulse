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
internal sealed class EntityFrameworkOutboxRepository<TContext> : IOutboxRepository, IDisposable
    where TContext : DbContext, IOutboxDbContext
{
    /// <summary>The DbContext used to build LINQ queries passed to the executor.</summary>
    private readonly TContext _context;

    /// <summary>The time provider used to generate consistent update and cutoff timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Provider-specific strategy that handles how entities are persisted
    /// (change-tracking + <c>SaveChangesAsync</c> vs. bulk <c>ExecuteUpdate</c> / <c>ExecuteDelete</c>).
    /// </summary>
    private readonly IOutboxRepositoryExecutor _executor;
    private bool _disposedValue;

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
        _executor = context.Database.ProviderName switch
        {
            // InMemory does not support ExecuteUpdate/ExecuteDelete at all.
            ProviderName.InMemory => new InMemoryOutboxRepositoryExecutor<TContext>(context, 1),
            // Oracle MySQL cannot apply value converters in ExecuteUpdateAsync parameters and
            // cannot translate a parameterised Guid collection into a SQL IN clause.
            ProviderName.OracleMySql => new MySqlOutboxRepositoryExecutor<TContext>(context, 1),
            ProviderName.Npgsql => new BulkOutboxRepositoryExecutor<TContext>(context, 1),
            _ => new BulkOutboxRepositoryExecutor<TContext>(context, Environment.ProcessorCount - 1),
        };
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

        var baseQuery = _context
            .OutboxMessages.Where(m =>
                m.Status == OutboxMessageStatus.Pending && (m.NextRetryAt == null || m.NextRetryAt <= now)
            )
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize);

        return await _executor
            .FetchAndMarkAsync(baseQuery, now, OutboxMessageStatus.Processing, cancellationToken)
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

        var baseQuery = _context
            .OutboxMessages.Where(m =>
                m.Status == OutboxMessageStatus.Failed
                && m.RetryCount < maxRetryCount
                && (m.NextRetryAt == null || m.NextRetryAt <= now)
            )
            .OrderBy(m => m.UpdatedAt)
            .Take(batchSize);

        return await _executor
            .FetchAndMarkAsync(baseQuery, now, OutboxMessageStatus.Processing, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        await _executor
            .UpdateByQueryAsync(
                _context.OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing),
                now,
                now,
                null,
                OutboxMessageStatus.Completed,
                0,
                null,
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

        await _executor
            .UpdateByIdsAsync(messageIds, now, now, null, OutboxMessageStatus.Completed, 0, null, cancellationToken)
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

        await _executor
            .UpdateByQueryAsync(
                _context.OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing),
                now,
                null,
                null,
                OutboxMessageStatus.Failed,
                1,
                errorMessage,
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

        await _executor
            .UpdateByQueryAsync(
                _context.OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing),
                now,
                null,
                nextRetryAt,
                OutboxMessageStatus.Failed,
                1,
                errorMessage,
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

        await _executor
            .UpdateByIdsAsync(
                messageIds,
                now,
                null,
                null,
                OutboxMessageStatus.Failed,
                1,
                errorMessage,
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

        await _executor
            .UpdateByQueryAsync(
                _context.OutboxMessages.Where(m => m.Id == messageId && m.Status == OutboxMessageStatus.Processing),
                now,
                null,
                null,
                OutboxMessageStatus.DeadLetter,
                0,
                errorMessage,
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

        await _executor
            .UpdateByIdsAsync(
                messageIds,
                now,
                null,
                null,
                OutboxMessageStatus.DeadLetter,
                1,
                errorMessage,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoffTime = _timeProvider.GetUtcNow().Subtract(olderThan);

        return await _executor
            .DeleteByQueryAsync(
                _context.OutboxMessages.Where(m =>
                    m.Status == OutboxMessageStatus.Completed && m.ProcessedAt < cutoffTime
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _executor.Dispose();
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
