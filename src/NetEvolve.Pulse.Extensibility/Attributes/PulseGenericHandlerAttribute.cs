namespace NetEvolve.Pulse.Extensibility.Attributes;

/// <summary>
/// Marks an open-generic handler class for automatic dependency injection registration as an
/// open-generic type by the Pulse source generator. Unlike <see cref="PulseHandlerAttribute"/>,
/// which requires a concrete closed type, this attribute instructs the generator to emit a
/// <c>typeof()</c>-based open-generic registration so that the DI container can resolve any
/// closed variant at runtime.
/// </summary>
/// <remarks>
/// <para>
/// The attribute is consumed at compile time only; it has no runtime effect beyond metadata.
/// </para>
/// <para>
/// <strong>Usage:</strong> Apply to an open-generic class that implements one or more Pulse
/// handler interfaces parameterized by the class's own type parameters. The generator emits a
/// registration of the form
/// <c>services.TryAddScoped(typeof(ICommandHandler&lt;,&gt;), typeof(MyHandler&lt;,&gt;))</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Open-generic command handler registered for any ICommand&lt;TResult&gt;
/// [PulseGenericHandler]
/// public class GenericCommandHandler&lt;TCommand, TResult&gt;
///     : ICommandHandler&lt;TCommand, TResult&gt;
///     where TCommand : ICommand&lt;TResult&gt;
/// {
///     public Task&lt;TResult&gt; HandleAsync(
///         TCommand command, CancellationToken cancellationToken) =&gt; ...;
/// }
///
/// // With explicit Singleton lifetime
/// [PulseGenericHandler(Lifetime = PulseServiceLifetime.Singleton)]
/// public class CachedQueryHandler&lt;TQuery, TResult&gt;
///     : IQueryHandler&lt;TQuery, TResult&gt;
///     where TQuery : IQuery&lt;TResult&gt;
/// {
///     public Task&lt;TResult&gt; HandleAsync(
///         TQuery query, CancellationToken cancellationToken) =&gt; ...;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PulseGenericHandlerAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the service lifetime for the handler registration.
    /// Defaults to <see cref="PulseServiceLifetime.Scoped"/>.
    /// </summary>
    public PulseServiceLifetime Lifetime { get; set; } = PulseServiceLifetime.Scoped;
}
