namespace NetEvolve.Pulse.SourceGeneration;

/// <summary>
/// Marks a handler class for automatic dependency injection registration by the Pulse source generator.
/// The generator inspects the annotated class for implementations of
/// <c>ICommandHandler&lt;,&gt;</c>, <c>IQueryHandler&lt;,&gt;</c>, or <c>IEventHandler&lt;&gt;</c> and emits
/// a compile-time <c>AddGeneratedPulseHandlers</c> extension method on <c>IServiceCollection</c>.
/// </summary>
/// <remarks>
/// <para>
/// The attribute is consumed at compile time only; it has no runtime effect beyond metadata.
/// </para>
/// <para>
/// <strong>Usage:</strong> Apply to any concrete class that implements one or more Pulse handler interfaces.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [PulseHandler]
/// public class CreateOrderCommandHandler
///     : ICommandHandler&lt;CreateOrderCommand, OrderResult&gt;
/// {
///     public Task&lt;OrderResult&gt; HandleAsync(
///         CreateOrderCommand command, CancellationToken cancellationToken) =&gt; ...;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PulseHandlerAttribute : Attribute;
