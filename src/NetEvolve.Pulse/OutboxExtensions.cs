namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring outbox services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class OutboxExtensions
{
    /// <summary>
    /// Adds core outbox services including the <see cref="IEventOutbox"/> implementation,
    /// <see cref="InMemoryMessageTransport"/>, and <see cref="OutboxProcessorHostedService"/>.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="OutboxOptions"/>.</param>
    /// <param name="configureProcessorOptions">Optional action to configure <see cref="OutboxProcessorOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="OutboxOptions"/> - Configuration options (Singleton)</description></item>
    /// <item><description><see cref="OutboxProcessorOptions"/> - Processor options (Singleton)</description></item>
    /// <item><description><see cref="IEventOutbox"/> as <see cref="OutboxEventStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IEventHandler{TEvent}"/> as <see cref="OutboxEventHandler{TEvent}"/> (Scoped, open-generic)</description></item>
    /// <item><description><see cref="IMessageTransport"/> as <see cref="InMemoryMessageTransport"/> (Singleton)</description></item>
    /// <item><description><see cref="ITopicNameResolver"/> as <see cref="DefaultTopicNameResolver"/> (Singleton)</description></item>
    /// <item><description><see cref="OutboxProcessorHostedService"/> (Hosted service)</description></item>
    /// <item><description><see cref="TimeProvider"/> - System time provider (Singleton)</description></item>
    /// </list>
    /// <para><strong>Usage:</strong></para>
    /// An <see cref="IOutboxRepository"/> implementation must be registered separately by calling
    /// <c>AddSqlServerOutbox</c>, <c>AddEntityFrameworkOutbox</c>, or a custom implementation.
    /// </remarks>
    public static IMediatorBuilder AddOutbox(
        this IMediatorBuilder configurator,
        Action<OutboxOptions>? configureOptions = null,
        Action<OutboxProcessorOptions>? configureProcessorOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        // Register options using the Options pattern for configurability
        // Always use Configure() to allow subsequent calls to modify options
        _ = services.AddOptions<OutboxOptions>();
        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        _ = services.AddOptions<OutboxProcessorOptions>();
        if (configureProcessorOptions is not null)
        {
            _ = services.Configure(configureProcessorOptions);
        }

        // Register TimeProvider if not already registered
        services.TryAddSingleton(TimeProvider.System);

        // Register core services
        services.TryAddScoped<IEventOutbox, OutboxEventStore>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IEventHandler<>), typeof(OutboxEventHandler<>)));
        services.TryAddSingleton<IMessageTransport, InMemoryMessageTransport>();
        services.TryAddSingleton<ITopicNameResolver, DefaultTopicNameResolver>();

        // Register background processor (TryAddEnumerable prevents duplicate registrations)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxProcessorHostedService>());

        return configurator;
    }

    /// <summary>
    /// Configures the outbox to use a custom <see cref="IMessageTransport"/> implementation.
    /// </summary>
    /// <typeparam name="TTransport">The transport implementation type.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Replaces any existing <see cref="IMessageTransport"/> registration.
    /// </remarks>
    public static IMediatorBuilder UseMessageTransport<TTransport>(this IMediatorBuilder configurator)
        where TTransport : class, IMessageTransport
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton<IMessageTransport, TTransport>();

        return configurator;
    }

    /// <summary>
    /// Configures the outbox to use a custom <see cref="IMessageTransport"/> factory.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="factory">The factory function to create the transport.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Replaces any existing <see cref="IMessageTransport"/> registration.
    /// </remarks>
    public static IMediatorBuilder UseMessageTransport(
        this IMediatorBuilder configurator,
        Func<IServiceProvider, IMessageTransport> factory
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(factory);

        var services = configurator.Services;

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton(factory);

        return configurator;
    }
}
