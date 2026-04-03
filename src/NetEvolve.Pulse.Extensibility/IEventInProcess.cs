namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Marker interface that signals an event must only be handled within the current process.
/// Events implementing this interface are excluded from outbox processing and external transports.
/// </summary>
/// <remarks>
/// <para>
/// Use this interface for events that represent in-process notifications, such as domain events
/// that trigger side effects within the same bounded context, and that must never be persisted
/// or forwarded to external message brokers.
/// </para>
/// <para><strong>Exclusion Semantics:</strong></para>
/// <list type="bullet">
/// <item><description>Events implementing <see cref="IEventInProcess"/> are skipped by the outbox dispatcher when <see cref="HandleInProcess"/> returns <see langword="true"/>.</description></item>
/// <item><description>They are dispatched synchronously to in-process handlers only.</description></item>
/// <item><description>No serialization, persistence, or transport occurs for these events.</description></item>
/// <item><description>Set <see cref="HandleInProcess"/> to <see langword="false"/> at runtime to route the event through the outbox instead.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public record StockReservedEvent : IEventInProcess
/// {
///     public string Id { get; init; } = Guid.NewGuid().ToString();
///     public DateTimeOffset? PublishedAt { get; set; }
///     public string? CorrelationId { get; set; }
///     public string ProductId { get; init; }
///     public int Quantity { get; init; }
///
///     // Override to route through outbox during a specific runtime condition.
///     public bool HandleInProcess => !SomeRuntimeCondition;
/// }
/// </code>
/// </example>
/// <seealso cref="IEvent"/>
/// <seealso cref="IEventHandler{TEvent}"/>
public interface IEventInProcess : IEvent
{
    /// <summary>
    /// Gets a value indicating whether this event must be handled in-process only.
    /// </summary>
    /// <value>
    /// <see langword="true"/> (default) to skip outbox processing and dispatch the event synchronously
    /// to in-process handlers only; <see langword="false"/> to route the event through the outbox
    /// as if it were a regular <see cref="IEvent"/>.
    /// </value>
    bool HandleInProcess => true;
}
