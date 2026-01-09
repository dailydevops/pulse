namespace NetEvolve.Pulse.Internals;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Internal implementation of <see cref="IMediatorConfigurator"/> that provides fluent configuration capabilities for the Pulse mediator.
/// This class is used during service registration to add interceptors and other cross-cutting concerns.
/// </summary>
internal sealed class MediatorConfigurator : IMediatorConfigurator
{
    /// <summary>
    /// The service collection being configured.
    /// </summary>
    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorConfigurator"/> class.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public MediatorConfigurator(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public IMediatorConfigurator AddActivityAndMetrics()
    {
        // Register the activity and metrics interceptor as a singleton for all request types
        _services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(ActivityAndMetricsInterceptor<,>))
        );

        return this;
    }
}
