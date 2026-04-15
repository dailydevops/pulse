namespace NetEvolve.Pulse.Extensibility.Attributes;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Marks an open-generic handler class for explicit DI registration of a specific message type.
/// Apply this attribute once per message type you want the open-generic handler registered for.
/// </summary>
/// <typeparam name="TMessage">
/// The message type to close and register the handler for.
/// Must implement one of the known Pulse message interfaces:
/// <c>ICommand</c>, <c>ICommand&lt;TResponse&gt;</c>, <c>IQuery&lt;TResponse&gt;</c>,
/// <c>IEvent</c>, or <c>IStreamQuery&lt;TResponse&gt;</c>.
/// </typeparam>
/// <remarks>
/// <para>
/// The attribute is consumed at compile time only; it has no runtime effect beyond metadata.
/// </para>
/// <para>
/// The source generator inspects <typeparamref name="TMessage"/> to determine the Pulse handler
/// interface kind and result type, then closes the annotated open-generic handler class and emits
/// a registration in the generated <c>AddGeneratedPulseHandlers</c> extension method.
/// </para>
/// <para>
/// <strong>Allowed uses:</strong> Multiple applications on the same class (one per message type).
/// Not inherited.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Register a generic command handler for two specific command types
/// [PulseHandler&lt;CreateOrderCommand&gt;]
/// [PulseHandler&lt;CancelOrderCommand&gt;]
/// public class GenericCommandHandler&lt;TCommand, TResult&gt;
///     : ICommandHandler&lt;TCommand, TResult&gt;
///     where TCommand : ICommand&lt;TResult&gt;
/// {
///     public Task&lt;TResult&gt; HandleAsync(
///         TCommand command, CancellationToken cancellationToken) =&gt; ...;
/// }
///
/// // Register a generic event handler with Singleton lifetime
/// [PulseHandler&lt;OrderShippedEvent&gt;(Lifetime = PulseServiceLifetime.Singleton)]
/// public class GenericAuditEventHandler&lt;TEvent&gt;
///     : IEventHandler&lt;TEvent&gt;
///     where TEvent : IEvent
/// {
///     public Task HandleAsync(TEvent message, CancellationToken cancellationToken) =&gt; ...;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "As designed.")]
public sealed class PulseHandlerAttribute<TMessage> : Attribute
{
    /// <summary>
    /// Gets or sets the service lifetime for this specific handler registration.
    /// Defaults to <see cref="PulseServiceLifetime.Scoped"/>.
    /// </summary>
    public PulseServiceLifetime Lifetime { get; set; } = PulseServiceLifetime.Scoped;
}
