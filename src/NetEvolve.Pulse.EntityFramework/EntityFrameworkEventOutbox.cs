namespace NetEvolve.Pulse;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

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
public sealed class EntityFrameworkEventOutbox<TContext> : IEventOutbox
    where TContext : DbContext, IOutboxDbContext
{
    private readonly TContext _context;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkEventOutbox{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    /// <param name="options">The outbox configuration options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public EntityFrameworkEventOutbox(TContext context, IOptions<OutboxOptions> options, TimeProvider timeProvider)
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

        var now = _timeProvider.GetUtcNow();

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.TryParse(message.Id, out var id) ? id : Guid.NewGuid(),
            EventType =
                message.GetType().AssemblyQualifiedName
                ?? throw new InvalidOperationException(
                    $"Cannot get assembly-qualified name for type: {message.GetType()}"
                ),
            Payload = JsonSerializer.Serialize(message, message.GetType(), _options.JsonSerializerOptions),
            CorrelationId = message.CorrelationId,
            CreatedAt = now,
            UpdatedAt = now,
            Status = OutboxMessageStatus.Pending,
        };

        _ = await _context.OutboxMessages.AddAsync(outboxMessage, cancellationToken).ConfigureAwait(false);
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
