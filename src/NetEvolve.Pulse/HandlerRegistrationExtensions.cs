namespace NetEvolve.Pulse;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Provides fluent extension methods for registering handlers with the Pulse mediator.
/// All methods in this class are AOT-compatible and trimming-safe.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// These extension methods provide a fluent, type-safe API for registering command handlers, query handlers,
/// and event handlers with explicit control over service lifetimes.
/// <para><strong>AOT Compatibility:</strong></para>
/// All registration methods use generic type parameters with DynamicallyAccessedMembers attributes to ensure
/// full compatibility with Native AOT compilation and IL trimming. No reflection is used at runtime.
/// <para><strong>Service Lifetimes:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Scoped (default):</strong> New instance per scope (e.g., HTTP request) - recommended for most handlers</description></item>
/// <item><description><strong>Transient:</strong> New instance on every resolution - use for stateless, lightweight handlers</description></item>
/// <item><description><strong>Singleton:</strong> Single instance for application lifetime - use for stateless, thread-safe handlers with caching</description></item>
/// </list>
/// <para><strong>Best Practices:</strong></para>
/// <list type="bullet">
/// <item><description>Use Scoped lifetime for handlers with database dependencies</description></item>
/// <item><description>Use Singleton lifetime for read-only handlers with caching</description></item>
/// <item><description>Register event handlers last to ensure all dependencies are available</description></item>
/// <item><description>Use method chaining for clean, readable configuration</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// services.AddPulse(config =>
/// {
///     config
///         .AddCommandHandler&lt;CreateOrderCommand, OrderResult, CreateOrderCommandHandler&gt;()
///         .AddCommandHandler&lt;UpdateProductCommand, Void, UpdateProductCommandHandler&gt;()
///         .AddQueryHandler&lt;GetOrderQuery, Order, GetOrderQueryHandler&gt;()
///         .AddQueryHandler&lt;GetCustomerQuery, Customer, GetCustomerQueryHandler&gt;(ServiceLifetime.Singleton)
///         .AddEventHandler&lt;OrderCreatedEvent, SendOrderConfirmationEmailHandler&gt;()
///         .AddEventHandler&lt;OrderCreatedEvent, UpdateInventoryHandler&gt;()
///         .AddEventHandler&lt;OrderCreatedEvent, NotifyWarehouseHandler&gt;()
///         .AddActivityAndMetrics();
/// });
/// </code>
/// </example>
public static class HandlerRegistrationExtensions
{
    /// <summary>
    /// Registers a command handler for the specified command type.
    /// </summary>
    /// <typeparam name="TCommand">The command type that implements <see cref="ICommand{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The response type returned by the command handler.</typeparam>
    /// <typeparam name="THandler">The handler implementation type that implements <see cref="ICommandHandler{TCommand, TResponse}"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="lifetime">The service lifetime for the handler (default: Scoped).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator"/> is null.</exception>
    /// <remarks>
    /// <para><strong>⚠️ IMPORTANT:</strong> Commands must have exactly one handler. Registering multiple handlers
    /// for the same command type will result in the last registered handler being used.</para>
    /// <para><strong>AOT Safety:</strong></para>
    /// This method is fully compatible with Native AOT compilation. The <c>DynamicallyAccessedMembers</c> attribute
    /// ensures the handler's public constructors are preserved during trimming.
    /// <para><strong>Performance:</strong></para>
    /// Command handlers are resolved from the DI container on each mediator call. Use appropriate lifetimes
    /// to balance memory usage and initialization overhead.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register with default Scoped lifetime
    /// config.AddCommandHandler&lt;CreateOrderCommand, OrderResult, CreateOrderCommandHandler&gt;();
    ///
    /// // Register with explicit Singleton lifetime for stateless handler
    /// config.AddCommandHandler&lt;GenerateReportCommand, Report, ReportGeneratorHandler&gt;(ServiceLifetime.Singleton);
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddCommandHandler<
        TCommand,
        TResponse,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler
    >(this IMediatorConfigurator configurator, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TCommand : ICommand<TResponse>
        where THandler : class, ICommandHandler<TCommand, TResponse>
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.Services.Add(
            new ServiceDescriptor(typeof(ICommandHandler<TCommand, TResponse>), typeof(THandler), lifetime)
        );

        return configurator;
    }

    /// <summary>
    /// Registers a query handler for the specified query type.
    /// </summary>
    /// <typeparam name="TQuery">The query type that implements <see cref="IQuery{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The response type returned by the query handler.</typeparam>
    /// <typeparam name="THandler">The handler implementation type that implements <see cref="IQueryHandler{TQuery, TResponse}"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="lifetime">The service lifetime for the handler (default: Scoped).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator"/> is null.</exception>
    /// <remarks>
    /// <para><strong>⚠️ IMPORTANT:</strong> Queries must have exactly one handler. Registering multiple handlers
    /// for the same query type will result in the last registered handler being used.</para>
    /// <para><strong>AOT Safety:</strong></para>
    /// This method is fully compatible with Native AOT compilation. The <c>DynamicallyAccessedMembers</c> attribute
    /// ensures the handler's public constructors are preserved during trimming.
    /// <para><strong>Caching Considerations:</strong></para>
    /// Query handlers that implement caching should typically use Singleton lifetime to share the cache instance.
    /// Ensure thread-safety for Singleton handlers.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register with default Scoped lifetime
    /// config.AddQueryHandler&lt;GetOrderQuery, Order, GetOrderQueryHandler&gt;();
    ///
    /// // Register with Singleton lifetime for cached read-only queries
    /// config.AddQueryHandler&lt;GetProductCatalogQuery, ProductCatalog, CachedProductCatalogHandler&gt;(ServiceLifetime.Singleton);
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddQueryHandler<
        TQuery,
        TResponse,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler
    >(this IMediatorConfigurator configurator, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TQuery : IQuery<TResponse>
        where THandler : class, IQueryHandler<TQuery, TResponse>
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.Services.Add(
            new ServiceDescriptor(typeof(IQueryHandler<TQuery, TResponse>), typeof(THandler), lifetime)
        );

        return configurator;
    }

    /// <summary>
    /// Registers an event handler for the specified event type.
    /// Multiple handlers can be registered for the same event type and will all be invoked in parallel.
    /// </summary>
    /// <typeparam name="TEvent">The event type that implements <see cref="IEvent"/>.</typeparam>
    /// <typeparam name="THandler">The handler implementation type that implements <see cref="IEventHandler{TEvent}"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="lifetime">The service lifetime for the handler (default: Scoped).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator"/> is null.</exception>
    /// <remarks>
    /// <para><strong>Multiple Handlers:</strong></para>
    /// Unlike commands and queries, events can have multiple registered handlers. All handlers for an event
    /// are executed in parallel for optimal throughput. If one handler fails, others will still execute.
    /// <para><strong>AOT Safety:</strong></para>
    /// This method is fully compatible with Native AOT compilation. The <c>DynamicallyAccessedMembers</c> attribute
    /// ensures the handler's public constructors are preserved during trimming.
    /// <para><strong>Execution Order:</strong></para>
    /// Event handlers are executed in parallel with no guaranteed order. Do not rely on execution sequence.
    /// If ordering is required, consider using a single coordinator handler that dispatches sequentially.
    /// <para><strong>Error Handling:</strong></para>
    /// If any event handler throws an exception, it will be logged but will not prevent other handlers from executing.
    /// The mediator will aggregate all exceptions and throw an <see cref="AggregateException"/> if any handlers fail.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register multiple handlers for the same event
    /// config
    ///     .AddEventHandler&lt;OrderCreatedEvent, SendOrderConfirmationEmailHandler&gt;()
    ///     .AddEventHandler&lt;OrderCreatedEvent, UpdateInventoryHandler&gt;()
    ///     .AddEventHandler&lt;OrderCreatedEvent, NotifyWarehouseHandler&gt;()
    ///     .AddEventHandler&lt;OrderCreatedEvent, UpdateAnalyticsHandler&gt;(ServiceLifetime.Singleton);
    ///
    /// // All four handlers will execute in parallel when OrderCreatedEvent is published
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddEventHandler<
        TEvent,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler
    >(this IMediatorConfigurator configurator, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.Services.Add(new ServiceDescriptor(typeof(IEventHandler<TEvent>), typeof(THandler), lifetime));

        return configurator;
    }
}
