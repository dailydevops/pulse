namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides fluent extension methods for registering HTTP correlation ID enrichment interceptors
/// with the Pulse mediator.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The HTTP correlation enrichment interceptors automatically propagate the correlation ID resolved
/// by <c>IHttpCorrelationAccessor</c> into every <see cref="IRequest{TResponse}"/> and <see cref="IEvent"/>
/// dispatched through the mediator, eliminating repetitive manual population at each call site.
/// <para><strong>Optional Dependency:</strong></para>
/// If <c>IHttpCorrelationAccessor</c> is not registered in the DI container (for example in a
/// background-service context), both interceptors pass through without modification or error.
/// <para><strong>Idempotency:</strong></para>
/// Calling <see cref="AddHttpCorrelationEnrichment"/> multiple times is safe — the interceptors are
/// registered via <c>TryAddEnumerable</c> and will not be duplicated.
/// </remarks>
public static class HttpCorrelationMediatorConfiguratorExtensions
{
    /// <summary>
    /// Registers HTTP correlation ID enrichment interceptors for all requests and events.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Registered Interceptors:</strong></para>
    /// <list type="bullet">
    /// <item>
    ///   <description>
    ///     <c>HttpCorrelationRequestInterceptor&lt;TRequest, TResponse&gt;</c> — enriches all
    ///     <see cref="IRequest{TResponse}"/> instances with the HTTP correlation ID.
    ///   </description>
    /// </item>
    /// <item>
    ///   <description>
    ///     <c>HttpCorrelationEventInterceptor&lt;TEvent&gt;</c> — enriches all <see cref="IEvent"/>
    ///     instances with the HTTP correlation ID.
    ///   </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(c =&gt; c.AddHttpCorrelationEnrichment());
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddHttpCorrelationEnrichment(this IMediatorConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(HttpCorrelationRequestInterceptor<,>))
        );

        configurator.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IEventInterceptor<>), typeof(HttpCorrelationEventInterceptor<>))
        );

        return configurator;
    }
}
