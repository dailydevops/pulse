namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides fluent extension methods for registering the built-in request timeout interceptor
/// with the Pulse mediator.
/// </summary>
/// <seealso cref="TimeoutRequestInterceptorOptions"/>
/// <seealso cref="ITimeoutRequest"/>
public static class TimeoutMediatorConfiguratorExtensions
{
    /// <summary>
    /// Registers the built-in <c>TimeoutRequestInterceptor</c> that enforces per-request deadlines
    /// using a linked <see cref="CancellationTokenSource"/>.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="globalTimeout">
    /// An optional global fallback timeout applied to <see cref="ITimeoutRequest"/> implementations
    /// that return <see langword="null"/> from <see cref="ITimeoutRequest.Timeout"/>.
    /// Requests that do not implement <see cref="ITimeoutRequest"/> are always passed through
    /// regardless of this value.
    /// When <see langword="null"/> (default), only requests implementing <see cref="ITimeoutRequest"/>
    /// with a non-<see langword="null"/> <see cref="ITimeoutRequest.Timeout"/> are subject to a deadline.
    /// </param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Timeout Resolution (for <see cref="ITimeoutRequest"/> requests only):</strong></para>
    /// <list type="number">
    /// <item><description><see cref="ITimeoutRequest.Timeout"/> — used when non-<see langword="null"/>.</description></item>
    /// <item><description><paramref name="globalTimeout"/> — used as fallback when <see cref="ITimeoutRequest.Timeout"/> is <see langword="null"/>.</description></item>
    /// <item><description>If neither is set, the interceptor is a transparent pass-through.</description></item>
    /// </list>
    /// Requests that do not implement <see cref="ITimeoutRequest"/> are always passed through.
    /// <para><strong>Cancellation Semantics:</strong></para>
    /// A <see cref="TimeoutException"/> is thrown only when the deadline is exceeded.
    /// Caller-initiated cancellations propagate as <see cref="OperationCanceledException"/> as usual.
    /// </remarks>
    /// <example>
    /// <para><strong>Without global timeout (only ITimeoutRequest requests with a non-null Timeout are affected):</strong></para>
    /// <code>
    /// services.AddPulse(c =&gt; c.AddRequestTimeout());
    /// </code>
    /// <para><strong>With global fallback timeout (ITimeoutRequest requests with a null Timeout use this as deadline):</strong></para>
    /// <code>
    /// services.AddPulse(c =&gt; c.AddRequestTimeout(TimeSpan.FromSeconds(30)));
    /// </code>
    /// </example>
    /// <seealso cref="ITimeoutRequest"/>
    /// <seealso cref="TimeoutRequestInterceptorOptions"/>
    public static IMediatorConfigurator AddRequestTimeout(
        this IMediatorConfigurator configurator,
        TimeSpan? globalTimeout = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        _ = configurator.Services.Configure<TimeoutRequestInterceptorOptions>(opts =>
            opts.GlobalTimeout = globalTimeout
        );

        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(TimeoutRequestInterceptor<,>))
        );

        return configurator;
    }
}
