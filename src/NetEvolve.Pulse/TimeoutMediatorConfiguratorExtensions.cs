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
    /// using a linked <see cref="System.Threading.CancellationTokenSource"/>.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="globalTimeout">
    /// An optional global fallback timeout applied to all requests that do not implement
    /// <see cref="ITimeoutRequest"/>.
    /// When <see langword="null"/> (default), only requests implementing <see cref="ITimeoutRequest"/>
    /// are subject to a deadline.
    /// </param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Timeout Resolution:</strong></para>
    /// <list type="number">
    /// <item><description>If the request implements <see cref="ITimeoutRequest"/>, its <see cref="ITimeoutRequest.Timeout"/> is used.</description></item>
    /// <item><description>Otherwise, <paramref name="globalTimeout"/> is applied (when provided).</description></item>
    /// <item><description>If neither is set, the interceptor is a transparent pass-through for that request.</description></item>
    /// </list>
    /// <para><strong>Cancellation Semantics:</strong></para>
    /// A <see cref="System.TimeoutException"/> is thrown only when the deadline is exceeded.
    /// Caller-initiated cancellations propagate as <see cref="System.OperationCanceledException"/> as usual.
    /// </remarks>
    /// <example>
    /// <para><strong>Without global timeout (only ITimeoutRequest requests are affected):</strong></para>
    /// <code>
    /// services.AddPulse(c =&gt; c.AddRequestTimeout());
    /// </code>
    /// <para><strong>With global fallback timeout (all requests are affected):</strong></para>
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
