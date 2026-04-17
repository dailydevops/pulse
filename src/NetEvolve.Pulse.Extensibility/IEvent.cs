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
/// <seealso cref="IMediatorSendOnly.PublishAsync{TEvent}"/>
public interface IEvent
{
    /// <summary>
    /// Gets or sets the causation identifier that records which command or event directly caused this event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set <c>CausationId</c> to the <c>Id</c> of the command or event that directly triggered the current event.
    /// Combined with <see cref="CorrelationId"/>, this enables full causal chain reconstruction.
    /// The mediator does <strong>not</strong> populate this value automatically — the caller is responsible.
    /// </para>
    /// <para><strong>Example causal chain:</strong></para>
    /// <code>
    /// PlaceOrder      (Command, Id: "cmd-1",  CorrelationId: "txn-42")
    ///   └─► OrderPlaced (Event,  Id: "evt-1",  CorrelationId: "txn-42", CausationId: "cmd-1")
    ///         └─► ReserveInventory (Command, Id: "cmd-2", CorrelationId: "txn-42", CausationId: "evt-1")
    ///               └─► InventoryReserved (Event, Id: "evt-2", CorrelationId: "txn-42", CausationId: "cmd-2")
    /// </code>
    /// </remarks>
    string? CausationId { get; set; }

    /// <summary>
    /// Gets or sets the correlation identifier for tracing related operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CorrelationId</c> groups all events and commands that belong to the same logical transaction or workflow.
    /// Use <see cref="CausationId"/> when you also need to reconstruct the exact cause-effect chain within that group.
    /// </para>
    /// </remarks>
    string? CorrelationId { get; set; }

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
