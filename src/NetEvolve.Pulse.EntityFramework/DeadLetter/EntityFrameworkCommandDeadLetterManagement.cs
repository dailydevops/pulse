namespace NetEvolve.Pulse.DeadLetter;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkCommandDeadLetterManagement{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    /// <param name="mediator">The mediator used to dispatch replayed commands.</param>
    /// <param name="payloadSerializer">The serializer used to deserialize stored payloads.</param>
    /// <param name="timeProvider">The time provider used to timestamp a failed replay.</param>
    public EntityFrameworkCommandDeadLetterManagement(
        TContext context,
        IMediatorSendOnly mediator,
        IPayloadSerializer payloadSerializer,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _mediator = mediator;
        _payloadSerializer = payloadSerializer;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommandDeadLetterEntry>> GetPendingAsync(
        int count = 50,
        int skip = 0,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);

        return await _context
            .CommandDeadLetterEntries.Where(e => e.Status == CommandDeadLetterStatus.New)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Skip(skip)
            .Take(count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandDeadLetterEntry?> GetEntryAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context
            .CommandDeadLetterEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    [RequiresUnreferencedCode(
        "Dead-letter replay resolves the persisted command type by name and dispatches it through reflection. The command type and its members might be removed by trimming."
    )]
    [RequiresDynamicCode(
        "Dead-letter replay closes generic methods over runtime command and response types, which can require dynamic code generation."
    )]
    public async Task ReplayAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await GetRequiredEntryAsync(id, cancellationToken).ConfigureAwait(false);
        if (entry.Status == CommandDeadLetterStatus.Dismissed)
        {
            throw new CommandDeadLetterEntryDismissedException(id);
        }

        entry.Status = CommandDeadLetterStatus.Replaying;
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await CommandDeadLetterReplayDispatcher
                .ReplayAsync(_mediator, _payloadSerializer, entry.CommandType, entry.Payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Not cancellable: the reset must also run when the replay was cancelled.
            try
            {
                DiscardPendingChanges();
                if (_context.Entry(entry).State == EntityState.Detached)
                {
                    // The handler cleared the change tracker.
                    _ = _context.Attach(entry);
                }

                entry.Status = CommandDeadLetterStatus.New;
                entry.AttemptCount++;
                entry.ExceptionType = ex.GetType().AssemblyQualifiedName;
                entry.ExceptionMessage = ex.Message;
                entry.OccurredAt = _timeProvider.GetUtcNow();
                _ = await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // A failed reset must not hide the replay failure: the entry stays in Replaying
                // and the original exception is rethrown below.
            }

            throw;
        }

        // Not cancellable: the command has already been executed.
        entry.Status = CommandDeadLetterStatus.Resolved;
        _ = await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DismissAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await GetRequiredEntryAsync(id, cancellationToken).ConfigureAwait(false);

        entry.Status = CommandDeadLetterStatus.Dismissed;
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandDeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
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
    /// Discards the unsaved changes a failed replay left in the shared context, so the reset
    /// neither persists them nor fails on them.
    /// </summary>
    /// <remarks>
    /// Changes the caller made before the replay were already saved together with the
    /// <see cref="CommandDeadLetterStatus.Replaying"/> status, so every pending change belongs to the
    /// replayed handler. Entities that are tracked without changes stay attached.
    /// </remarks>
    private void DiscardPendingChanges()
    {
        foreach (
            var tracked in _context
                .ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList()
        )
        {
            if (tracked.State == EntityState.Added)
            {
                tracked.State = EntityState.Detached;
                continue;
            }

            tracked.CurrentValues.SetValues(tracked.OriginalValues);
            tracked.State = EntityState.Unchanged;
        }
    }

    /// <summary>
    /// Loads the command dead letter entry identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The identifier of the dead letter entry to load.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The loaded <see cref="CommandDeadLetterEntry"/>.</returns>
    /// <exception cref="CommandDeadLetterEntryNotFoundException">No entry with the given <paramref name="id"/> exists.</exception>
    private async Task<CommandDeadLetterEntry> GetRequiredEntryAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await _context
            .CommandDeadLetterEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entry ?? throw new CommandDeadLetterEntryNotFoundException(id);
    }
}
