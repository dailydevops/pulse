namespace NetEvolve.Pulse;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides fluent extension methods for registering event filters with the Pulse mediator.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Event filters allow conditional event delivery without modifying handler code.
/// Registered filters are evaluated by <see cref="EventFilterInterceptor{TEvent}"/> before handlers are invoked.
/// <para><strong>AND Semantics:</strong></para>
/// All registered filters must return <see langword="true"/> for the handler to be invoked.
/// If any filter returns <see langword="false"/>, the handler is silently skipped.
/// <para><strong>Zero-Overhead Pass-Through:</strong></para>
/// If no filters are registered for an event type, the <see cref="EventFilterInterceptor{TEvent}"/>
/// is still registered but delegates immediately to the handler at negligible cost.
/// <para><strong>Idempotency:</strong></para>
/// The <see cref="EventFilterInterceptor{TEvent}"/> is registered via <c>TryAddEnumerable</c>
/// and will not be duplicated, regardless of how many filters are added.
/// </remarks>
public static class EventFilterExtensions
{
    /// <summary>
    /// Registers an <see cref="IEventFilter{TEvent}"/> implementation for the specified event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type that implements <see cref="IEvent"/>.</typeparam>
    /// <typeparam name="TFilter">The filter implementation type that implements <see cref="IEventFilter{TEvent}"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="lifetime">The service lifetime for the filter (default: Scoped).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// services.AddPulse(config =>
    ///     config.AddEventFilter&lt;OrderCreatedEvent, HighValueOrderFilter&gt;()
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddEventFilter<
        TEvent,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFilter
    >(this IMediatorBuilder configurator, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TEvent : IEvent
        where TFilter : class, IEventFilter<TEvent>
    {
        ArgumentNullException.ThrowIfNull(configurator);

        EnsureFilterInterceptorRegistered(configurator.Services);

        configurator.Services.Add(new ServiceDescriptor(typeof(IEventFilter<TEvent>), typeof(TFilter), lifetime));

        return configurator;
    }

    /// <summary>
    /// Registers an inline predicate as an <see cref="IEventFilter{TEvent}"/> for the specified event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type that implements <see cref="IEvent"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="predicate">
    /// A function that receives the event and a cancellation token, and returns <see langword="true"/>
    /// if the event should be handled; otherwise, <see langword="false"/>.
    /// </param>
    /// <param name="lifetime">The service lifetime for the filter (default: Scoped).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configurator"/> or <paramref name="predicate"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// services.AddPulse(config =>
    ///     config.AddEventFilter&lt;OrderCreatedEvent&gt;(
    ///         (evt, ct) => ValueTask.FromResult(evt.IsHighValue)
    ///     )
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddEventFilter<TEvent>(
        this IMediatorBuilder configurator,
        Func<TEvent, CancellationToken, ValueTask<bool>> predicate,
        ServiceLifetime lifetime = ServiceLifetime.Scoped
    )
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(predicate);

        EnsureFilterInterceptorRegistered(configurator.Services);

        configurator.Services.Add(
            new ServiceDescriptor(
                typeof(IEventFilter<TEvent>),
                _ => new PredicateEventFilter<TEvent>(predicate),
                lifetime
            )
        );

        return configurator;
    }

    private static void EnsureFilterInterceptorRegistered(IServiceCollection services) =>
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IEventInterceptor<>), typeof(EventFilterInterceptor<>))
        );
}
