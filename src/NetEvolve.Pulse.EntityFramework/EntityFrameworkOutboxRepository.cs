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
public sealed class EntityFrameworkOutboxRepository<TContext> : IOutboxRepository
    where TContext : DbContext, IOutboxDbContext
{
    private readonly TContext _context;
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

        // Get pending messages and update status to Processing in a single query
        var messages = await _context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Mark as processing
        foreach (var message in messages)
        {
            message.Status = OutboxMessageStatus.Processing;
            message.UpdatedAt = now;
        }

        if (messages.Count > 0)
        {
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        var messages = await _context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Failed && m.RetryCount < maxRetryCount)
            .OrderBy(m => m.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Mark as processing
        foreach (var message in messages)
        {
            message.Status = OutboxMessageStatus.Processing;
            message.UpdatedAt = now;
        }

        if (messages.Count > 0)
        {
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        var message = await _context
            .OutboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is not null)
        {
            message.Status = OutboxMessageStatus.Completed;
            message.ProcessedAt = now;
            message.UpdatedAt = now;

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task MarkAsFailedAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        var message = await _context
            .OutboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is not null)
        {
            message.Status = OutboxMessageStatus.Failed;
            message.RetryCount++;
            message.Error = errorMessage;
            message.UpdatedAt = now;

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task MarkAsDeadLetterAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();

        var message = await _context
            .OutboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is not null)
        {
            message.Status = OutboxMessageStatus.DeadLetter;
            message.Error = errorMessage;
            message.UpdatedAt = now;

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoffTime = _timeProvider.GetUtcNow().Subtract(olderThan);

        var messagesToDelete = await _context
            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Completed && m.ProcessedAt < cutoffTime)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (messagesToDelete.Count > 0)
        {
            _context.OutboxMessages.RemoveRange(messagesToDelete);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return messagesToDelete.Count;
    }
}
