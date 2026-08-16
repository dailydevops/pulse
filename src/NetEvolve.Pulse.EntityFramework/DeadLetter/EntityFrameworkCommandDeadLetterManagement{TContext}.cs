namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Entity Framework Core implementation of <see cref="ICommandDeadLetterManagement"/>.
/// Provides pending-entry inspection, replay, dismissal, and statistics queries using any EF Core database provider.
/// </summary>
/// <remarks>
/// All read and write operations run directly against <see cref="ICommandDeadLetterDbContext.CommandDeadLetterEntries"/>
/// using plain LINQ queries and <c>SaveChangesAsync</c>, and are therefore provider-agnostic. Replay dispatch
/// is delegated to the shared <see cref="CommandDeadLetterReplayDispatcher"/>.
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="ICommandDeadLetterDbContext"/>.</typeparam>
internal sealed class EntityFrameworkCommandDeadLetterManagement<TContext> : ICommandDeadLetterManagement
    where TContext : DbContext, ICommandDeadLetterDbContext
{
    private readonly TContext _context;
    private readonly IMediatorSendOnly _mediator;
    private readonly IPayloadSerializer _payloadSerializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkCommandDeadLetterManagement{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    /// <param name="mediator">The mediator used to dispatch replayed commands.</param>
    /// <param name="payloadSerializer">The serializer used to deserialize stored payloads.</param>
    public EntityFrameworkCommandDeadLetterManagement(
        TContext context,
        IMediatorSendOnly mediator,
        IPayloadSerializer payloadSerializer
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(payloadSerializer);

        _context = context;
        _mediator = mediator;
        _payloadSerializer = payloadSerializer;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommandDeadLetterEntry>> GetPendingAsync(
        int count = 50,
        CancellationToken cancellationToken = default
    ) =>
        await _context
            .CommandDeadLetterEntries.Where(e => e.Status == CommandDeadLetterStatus.New)
            .OrderBy(e => e.OccurredAt)
            .Take(count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task ReplayAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = await GetEntryAsync(id, cancellationToken).ConfigureAwait(false);

        entry.Status = CommandDeadLetterStatus.Replaying;
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await CommandDeadLetterReplayDispatcher
            .ReplayAsync(_mediator, _payloadSerializer, entry.CommandType, entry.Payload, cancellationToken)
            .ConfigureAwait(false);

        entry.Status = CommandDeadLetterStatus.Resolved;
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DismissAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = await GetEntryAsync(id, cancellationToken).ConfigureAwait(false);

        entry.Status = CommandDeadLetterStatus.Dismissed;
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandDeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var counts = await _context
            .CommandDeadLetterEntries.GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken)
            .ConfigureAwait(false);

        return new CommandDeadLetterStatistics(
            NewCount: counts.GetValueOrDefault(CommandDeadLetterStatus.New),
            ReplayingCount: counts.GetValueOrDefault(CommandDeadLetterStatus.Replaying),
            ResolvedCount: counts.GetValueOrDefault(CommandDeadLetterStatus.Resolved),
            DismissedCount: counts.GetValueOrDefault(CommandDeadLetterStatus.Dismissed)
        );
    }

    /// <summary>
    /// Loads the command dead letter entry identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The identifier of the dead letter entry to load.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The loaded <see cref="CommandDeadLetterEntry"/>.</returns>
    /// <exception cref="KeyNotFoundException">No entry with the given <paramref name="id"/> exists.</exception>
    private async Task<CommandDeadLetterEntry> GetEntryAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = await _context
            .CommandDeadLetterEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entry ?? throw new KeyNotFoundException($"No command dead letter entry with id '{id}' was found.");
    }
}
