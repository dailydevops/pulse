namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides extension methods for registering the concurrent command guard interceptor
/// with the Pulse mediator.
/// </summary>
/// <seealso cref="IExclusiveCommand{TResponse}"/>
/// <seealso cref="IExclusiveCommand"/>
public static class ConcurrentCommandGuardMediatorBuilderExtensions
{
    /// <summary>
    /// Registers the concurrent command guard interceptor that enforces exclusive (non-concurrent)
    /// execution for commands implementing <see cref="IExclusiveCommand{TResponse}"/>.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Behavior:</strong></para>
    /// Commands that implement <see cref="IExclusiveCommand{TResponse}"/> are serialized per command type using
    /// a <see cref="System.Threading.SemaphoreSlim"/>(1,1). All other commands pass through with zero overhead.
    /// <para><strong>Registration:</strong></para>
    /// Calling <see cref="AddConcurrentCommandGuard"/> multiple times is safe — the interceptor is registered
    /// via <c>TryAddEnumerable</c> and will not be duplicated.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(c =&gt; c.AddConcurrentCommandGuard());
    /// </code>
    /// </example>
    public static IMediatorBuilder AddConcurrentCommandGuard(this IMediatorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(ConcurrentCommandGuardInterceptor<,>))
        );

        return builder;
    }
}
