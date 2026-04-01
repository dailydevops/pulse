namespace NetEvolve.Pulse;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Provides extension methods for configuring event dispatchers with the Pulse mediator.
/// </summary>
/// <seealso cref="IEventDispatcher"/>
public static class EventDispatcherExtensions
{
    /// <summary>
    /// Configures a custom event dispatcher to control how events are dispatched to their handlers.
    /// </summary>
    /// <typeparam name="TDispatcher">The type of event dispatcher. Must implement <see cref="IEventDispatcher"/>.</typeparam>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="lifetime">The service lifetime for the dispatcher. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder UseDefaultEventDispatcher<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDispatcher
    >(this IMediatorBuilder builder, ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TDispatcher : class, IEventDispatcher
    {
        ArgumentNullException.ThrowIfNull(builder);

        var existingDescriptor = builder.Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null
        );
        if (existingDescriptor is not null)
        {
            _ = builder.Services.Remove(existingDescriptor);
        }

        builder.Services.Add(new ServiceDescriptor(typeof(IEventDispatcher), typeof(TDispatcher), lifetime));

        return builder;
    }

    /// <summary>
    /// Configures a custom event dispatcher using a factory delegate for custom instantiation.
    /// </summary>
    /// <typeparam name="TDispatcher">The type of event dispatcher. Must implement <see cref="IEventDispatcher"/>.</typeparam>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="factory">A factory delegate that receives the <see cref="IServiceProvider"/> and returns the dispatcher instance.</param>
    /// <param name="lifetime">The service lifetime for the dispatcher. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder UseDefaultEventDispatcher<TDispatcher>(
        this IMediatorBuilder builder,
        Func<IServiceProvider, TDispatcher> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TDispatcher : class, IEventDispatcher
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        var existingDescriptor = builder.Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && d.ServiceKey is null
        );
        if (existingDescriptor is not null)
        {
            _ = builder.Services.Remove(existingDescriptor);
        }

        builder.Services.Add(new ServiceDescriptor(typeof(IEventDispatcher), factory, lifetime));

        return builder;
    }

    /// <summary>
    /// Configures a custom event dispatcher for a specific event type using keyed services.
    /// </summary>
    /// <typeparam name="TEvent">The type of event this dispatcher handles.</typeparam>
    /// <typeparam name="TDispatcher">The type of event dispatcher. Must implement <see cref="IEventDispatcher"/>.</typeparam>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="lifetime">The service lifetime for the dispatcher. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder UseEventDispatcherFor<
        TEvent,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDispatcher
    >(this IMediatorBuilder builder, ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TEvent : IEvent
        where TDispatcher : class, IEventDispatcher
    {
        ArgumentNullException.ThrowIfNull(builder);

        var eventType = typeof(TEvent);

        var existingDescriptor = builder.Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, eventType)
        );
        if (existingDescriptor is not null)
        {
            _ = builder.Services.Remove(existingDescriptor);
        }

        builder.Services.Add(new ServiceDescriptor(typeof(IEventDispatcher), eventType, typeof(TDispatcher), lifetime));

        return builder;
    }

    /// <summary>
    /// Configures a custom event dispatcher for a specific event type using a factory delegate.
    /// </summary>
    /// <typeparam name="TEvent">The type of event this dispatcher handles.</typeparam>
    /// <typeparam name="TDispatcher">The type of event dispatcher. Must implement <see cref="IEventDispatcher"/>.</typeparam>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="factory">A factory delegate that receives the <see cref="IServiceProvider"/> and returns the dispatcher instance.</param>
    /// <param name="lifetime">The service lifetime for the dispatcher. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder UseEventDispatcherFor<TEvent, TDispatcher>(
        this IMediatorBuilder builder,
        Func<IServiceProvider, TDispatcher> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TEvent : IEvent
        where TDispatcher : class, IEventDispatcher
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        var eventType = typeof(TEvent);

        var existingDescriptor = builder.Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventDispatcher) && Equals(d.ServiceKey, eventType)
        );
        if (existingDescriptor is not null)
        {
            _ = builder.Services.Remove(existingDescriptor);
        }

        builder.Services.Add(
            new ServiceDescriptor(typeof(IEventDispatcher), eventType, (sp, _) => factory(sp), lifetime)
        );

        return builder;
    }
}
