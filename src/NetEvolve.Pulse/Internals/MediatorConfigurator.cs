namespace NetEvolve.Pulse.Internals;

using System.Diagnostics.CodeAnalysis;
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

    /// <inheritdoc />
    public IMediatorConfigurator UseDefaultEventDispatcher<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDispatcher
    >(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TDispatcher : class, IEventDispatcher
    {
        // Remove any existing global dispatcher registration to ensure only one is active
        // Note: ServiceKey is null check ensures we don't remove keyed (event-specific) dispatchers
        var existingDescriptor = Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null
        );
        if (existingDescriptor is not null)
        {
            _ = Services.Remove(existingDescriptor);
        }

        Services.Add(new ServiceDescriptor(typeof(IEventDispatcher), typeof(TDispatcher), lifetime));

        return this;
    }

    /// <inheritdoc />
    public IMediatorConfigurator UseDefaultEventDispatcher<TDispatcher>(
        Func<IServiceProvider, TDispatcher> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TDispatcher : class, IEventDispatcher
    {
        ArgumentNullException.ThrowIfNull(factory);

        // Remove any existing global dispatcher registration to ensure only one is active
        // Note: ServiceKey is null check ensures we don't remove keyed (event-specific) dispatchers
        var existingDescriptor = Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null
        );
        if (existingDescriptor is not null)
        {
            _ = Services.Remove(existingDescriptor);
        }

        Services.Add(new ServiceDescriptor(typeof(IEventDispatcher), factory, lifetime));

        return this;
    }

    /// <inheritdoc />
    public IMediatorConfigurator UseEventDispatcherFor<
        TEvent,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDispatcher
    >(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TEvent : IEvent
        where TDispatcher : class, IEventDispatcher
    {
        var eventType = typeof(TEvent);

        // Remove any existing keyed dispatcher registration for this event type
        var existingDescriptor = Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, eventType)
        );
        if (existingDescriptor is not null)
        {
            _ = Services.Remove(existingDescriptor);
        }

        // Register as keyed service with event type as key
        Services.Add(new ServiceDescriptor(typeof(IEventDispatcher), eventType, typeof(TDispatcher), lifetime));

        return this;
    }

    /// <inheritdoc />
    public IMediatorConfigurator UseEventDispatcherFor<TEvent, TDispatcher>(
        Func<IServiceProvider, TDispatcher> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TEvent : IEvent
        where TDispatcher : class, IEventDispatcher
    {
        ArgumentNullException.ThrowIfNull(factory);

        var eventType = typeof(TEvent);

        // Remove any existing keyed dispatcher registration for this event type
        var existingDescriptor = Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, eventType)
        );
        if (existingDescriptor is not null)
        {
            _ = Services.Remove(existingDescriptor);
        }

        // Register as keyed service with event type as key using factory
        Services.Add(new ServiceDescriptor(typeof(IEventDispatcher), eventType, (sp, _) => factory(sp), lifetime));

        return this;
    }
}
