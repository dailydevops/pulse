namespace NetEvolve.Pulse.Internals;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Internal implementation of <see cref="IMediatorConfigurator"/> that provides fluent configuration capabilities for the Pulse mediator.
/// This class is used during service registration to add interceptors and other cross-cutting concerns.
/// </summary>
internal sealed class MediatorConfigurator : IMediatorConfigurator
{
    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorConfigurator"/> class.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public MediatorConfigurator(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <inheritdoc />
    public IMediatorConfigurator AddActivityAndMetrics()
    {
        // Register the activity and metrics interceptor as a singleton for all event types
        Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IEventInterceptor<>), typeof(ActivityAndMetricsEventInterceptor<>))
        );
        // Register the activity and metrics interceptor as a singleton for all request types
        Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(ActivityAndMetricsRequestInterceptor<,>))
        );

        return this;
    }
}
