namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Entity Framework Core implementation of <see cref="IOutboxManagement"/>.
/// Provides dead-letter inspection, replay, and statistics queries using any EF Core database provider.
/// </summary>
/// <remarks>
/// Read and write operations are dispatched to a provider-specific <see cref="IOutboxManagementExecutor"/>
/// selected at construction time. The count query and statistics aggregation run directly against
/// <see cref="IOutboxDbContext.OutboxMessages"/> and are provider-agnostic.
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class EntityFrameworkOutboxManagement<TContext> : IOutboxManagement
    where TContext : DbContext, IOutboxDbContext
{
    /// <summary>The DbContext used for all LINQ-to-SQL query and update operations.</summary>
    private readonly TContext _context;

    /// <summary>The time provider used to generate consistent update timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Provider-specific strategy that handles how read and write operations are executed
    /// (<c>ExecuteUpdateAsync</c> / compiled queries vs. plain LINQ + <c>SaveChangesAsync</c>).
    /// </summary>
    private readonly IOutboxManagementExecutor _executor;

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
        _executor = context.Database.ProviderName switch
        {
            // InMemory does not support ExecuteUpdate/ExecuteDelete at all.
            ProviderName.InMemory => new TrackingOutboxManagementExecutor<TContext>(context),
            // Oracle MySQL cannot apply value converters in ExecuteUpdateAsync parameters.
            ProviderName.OracleMySql => new TrackingOutboxManagementExecutor<TContext>(context),
            _ => new BulkOutboxManagementExecutor<TContext>(context),
        };
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

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        if (page > int.MaxValue / pageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "The requested page is too large.");
        }

        return await _executor
            .GetDeadLetterMessages(page * pageSize, pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OutboxMessage?> GetDeadLetterMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default
    ) => _executor.GetDeadLetterMessageAsync(messageId, cancellationToken);

    /// <inheritdoc />
    public Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default) =>
        _context.OutboxMessages.LongCountAsync(m => m.Status == OutboxMessageStatus.DeadLetter, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ReplayMessageAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        _executor.ReplayByIdAsync(messageId, _timeProvider.GetUtcNow(), cancellationToken);

    /// <inheritdoc />
    public Task<int> ReplayAllDeadLetterAsync(CancellationToken cancellationToken = default) =>
        _executor.ReplayAllAsync(_timeProvider.GetUtcNow(), cancellationToken);

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
