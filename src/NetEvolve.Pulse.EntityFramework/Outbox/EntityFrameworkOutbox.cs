namespace NetEvolve.Pulse.Outbox;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Entity Framework Core implementation of <see cref="IEventOutbox"/> that stores events
/// using the DbContext and participates in ambient transactions.
/// </summary>
/// <remarks>
/// <para><strong>Transaction Integration:</strong></para>
/// Events stored via this class automatically participate in the current DbContext transaction.
/// If the transaction is rolled back, the event is also discarded.
/// <para><strong>Usage Pattern:</strong></para>
/// Use within a DbContext scope where business operations and event storage share the same transaction.
/// </remarks>
/// <example>
/// <code>
/// public class OrderService
/// {
///     private readonly MyDbContext _context;
///     private readonly IEventOutbox _outbox;
///
///     public async Task CreateOrderAsync(Order order, CancellationToken ct)
///     {
///         await using var transaction = await _context.Database.BeginTransactionAsync(ct);
///
///         _context.Orders.Add(order);
///         await _context.SaveChangesAsync(ct);
///
///         await _outbox.StoreAsync(new OrderCreatedEvent { OrderId = order.Id }, ct);
///
///         await transaction.CommitAsync(ct);
///     }
/// }
/// </code>
/// </example>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
internal sealed class EntityFrameworkOutbox<TContext> : IEventOutbox
    where TContext : DbContext, IOutboxDbContext
{
    /// <summary>The DbContext used for all database operations within the current scope.</summary>
    private readonly TContext _context;

    /// <summary>The resolved outbox options controlling serialization and table configuration.</summary>
    private readonly OutboxOptions _options;

    /// <summary>The time provider used to generate consistent creation and update timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkOutbox{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    /// <param name="options">The outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public EntityFrameworkOutbox(TContext context, IOptions<OutboxOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();

        var correlationId = message.CorrelationId;

        if (correlationId is { Length: > OutboxMessageSchema.MaxLengths.CorrelationId })
        {
            throw new InvalidOperationException(
                $"CorrelationId exceeds the maximum length of {OutboxMessageSchema.MaxLengths.CorrelationId} characters defined by the OutboxMessage schema. "
                    + "Provide a shorter correlation identifier to comply with the database constraint."
            );
        }

        var now = _timeProvider.GetUtcNow();
        var outboxMessage = new OutboxMessage
        {
            Id = message.ToOutboxId(),
            EventType = messageType,
            Payload = JsonSerializer.Serialize(message, messageType, _options.JsonSerializerOptions),
            CorrelationId = correlationId,
            CreatedAt = now,
            UpdatedAt = now,
            Status = OutboxMessageStatus.Pending,
        };

        _ = await _context.OutboxMessages.AddAsync(outboxMessage, cancellationToken).ConfigureAwait(false);
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
