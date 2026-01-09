namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents an event that can be published through the mediator to notify multiple handlers.
/// Events are immutable notifications where multiple subscribers may react independently.
/// </summary>
/// <remarks>
/// ⚠️ Event handlers execute in parallel and should be idempotent. They should not depend on execution order.
/// Use past-tense names (OrderCreated, PaymentProcessed, UserRegistered).
/// </remarks>
/// <example>
/// <code>
/// public record OrderCreatedEvent : IEvent
/// {
///     public string Id { get; init; } = Guid.NewGuid().ToString();
///     public DateTimeOffset? PublishedAt { get; set; }
///     public string OrderId { get; init; }
///     public decimal TotalAmount { get; init; }
/// }
///
/// public class OrderCreatedEmailHandler : IEventHandler&lt;OrderCreatedEvent&gt;
/// {
///     private readonly IEmailService _emailService;
///
///     public async Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken)
///     {
///         await _emailService.SendOrderConfirmationAsync(message.OrderId, cancellationToken);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IEventHandler{TEvent}"/>
/// <seealso cref="IMediator.PublishAsync{TEvent}"/>
public interface IEvent
{
    /// <summary>
    /// Gets the unique identifier for this event instance.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets or sets the timestamp when this event was published.
    /// Automatically set by the mediator.
    /// </summary>
    DateTimeOffset? PublishedAt { get; set; }
}
