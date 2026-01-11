namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a handler for processing events that supports priority-based ordering.
/// Handlers implementing this interface are ordered by <see cref="Priority"/> before execution.
/// </summary>
/// <typeparam name="TEvent">The type of event to handle.</typeparam>
/// <remarks>
/// <para><strong>Priority System:</strong></para>
/// <list type="bullet">
/// <item><description>Lower values execute first (0 = highest priority)</description></item>
/// <item><description>Handlers with equal priority execute in registration order</description></item>
/// <item><description>Handlers not implementing this interface are treated as priority <see cref="int.MaxValue"/></description></item>
/// </list>
/// <para><strong>Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>Validation handlers that must run before business logic</description></item>
/// <item><description>Audit handlers that must run last</description></item>
/// <item><description>Critical notification handlers that should execute first</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // High-priority validation handler
/// public class OrderValidationHandler : IPrioritizedEventHandler&lt;OrderCreatedEvent&gt;
/// {
///     public int Priority =&gt; 0; // Runs first
///
///     public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken)
///     {
///         // Validate order before other handlers process it
///     }
/// }
///
/// // Low-priority audit handler
/// public class OrderAuditHandler : IPrioritizedEventHandler&lt;OrderCreatedEvent&gt;
/// {
///     public int Priority =&gt; 1000; // Runs last
///
///     public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken)
///     {
///         // Log audit trail after all processing
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IEventHandler{TEvent}"/>
/// <seealso cref="IEventDispatcher"/>
public interface IPrioritizedEventHandler<in TEvent> : IEventHandler<TEvent>
    where TEvent : IEvent
{
    /// <summary>
    /// Gets the execution priority of this handler. Lower values indicate higher priority.
    /// </summary>
    /// <value>
    /// An integer where lower values execute first. Default recommended range is 0-1000.
    /// </value>
    /// <remarks>
    /// <para><strong>Priority Guidelines:</strong></para>
    /// <list type="bullet">
    /// <item><description>0-99: Critical handlers (validation, security)</description></item>
    /// <item><description>100-499: High priority (core business logic)</description></item>
    /// <item><description>500-699: Normal priority (standard processing)</description></item>
    /// <item><description>700-899: Low priority (notifications, non-critical)</description></item>
    /// <item><description>900+: Deferred handlers (audit, cleanup)</description></item>
    /// </list>
    /// </remarks>
    int Priority { get; }
}
