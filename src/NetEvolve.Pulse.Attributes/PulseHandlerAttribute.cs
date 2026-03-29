namespace NetEvolve.Pulse.Attributes;

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
/// // Register with default Scoped lifetime
/// [PulseHandler]
/// public class CreateOrderCommandHandler
///     : ICommandHandler&lt;CreateOrderCommand, OrderResult&gt;
/// {
///     public Task&lt;OrderResult&gt; HandleAsync(
///         CreateOrderCommand command, CancellationToken cancellationToken) =&gt; ...;
/// }
///
/// // Register with explicit Singleton lifetime
/// [PulseHandler(Lifetime = PulseServiceLifetime.Singleton)]
/// public class GetCachedDataQueryHandler
///     : IQueryHandler&lt;GetCachedDataQuery, CachedData&gt;
/// {
///     public Task&lt;CachedData&gt; HandleAsync(
///         GetCachedDataQuery query, CancellationToken cancellationToken) =&gt; ...;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PulseHandlerAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the service lifetime for the handler registration.
    /// Defaults to <see cref="PulseServiceLifetime.Scoped"/>.
    /// </summary>
    public PulseServiceLifetime Lifetime { get; set; } = PulseServiceLifetime.Scoped;
}
