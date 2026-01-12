namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Extension methods for configuring outbox services on <see cref="IMediatorConfigurator"/>.
/// </summary>
public static class OutboxMediatorConfiguratorExtensions
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
    /// <item><description><see cref="IMessageTransport"/> as <see cref="InMemoryMessageTransport"/> (Scoped)</description></item>
    /// <item><description><see cref="OutboxProcessorHostedService"/> (Hosted service)</description></item>
    /// <item><description><see cref="TimeProvider"/> - System time provider (Singleton)</description></item>
    /// </list>
    /// <para><strong>Usage:</strong></para>
    /// An <see cref="IOutboxRepository"/> implementation must be registered separately by calling
    /// <c>AddSqlServerOutbox</c>, <c>AddEntityFrameworkOutbox</c>, or a custom implementation.
    /// </remarks>
    public static IMediatorConfigurator AddOutbox(
        this IMediatorConfigurator configurator,
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
        services.TryAddScoped<IMessageTransport, InMemoryMessageTransport>();

        // Register background processor
        _ = services.AddHostedService<OutboxProcessorHostedService>();

        return configurator;
    }

    /// <summary>
    /// Configures the outbox to use a custom <see cref="IMessageTransport"/> implementation.
    /// </summary>
    /// <typeparam name="TTransport">The transport implementation type.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="lifetime">The service lifetime for the transport. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Replaces any existing <see cref="IMessageTransport"/> registration.
    /// </remarks>
    public static IMediatorConfigurator UseMessageTransport<TTransport>(
        this IMediatorConfigurator configurator,
        ServiceLifetime lifetime = ServiceLifetime.Scoped
    )
        where TTransport : class, IMessageTransport
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        var descriptor = new ServiceDescriptor(typeof(IMessageTransport), typeof(TTransport), lifetime);
        services.Add(descriptor);

        return configurator;
    }

    /// <summary>
    /// Configures the outbox to use a custom <see cref="IMessageTransport"/> factory.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="factory">The factory function to create the transport.</param>
    /// <param name="lifetime">The service lifetime for the transport. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Replaces any existing <see cref="IMessageTransport"/> registration.
    /// </remarks>
    public static IMediatorConfigurator UseMessageTransport(
        this IMediatorConfigurator configurator,
        Func<IServiceProvider, IMessageTransport> factory,
        ServiceLifetime lifetime = ServiceLifetime.Scoped
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

        var descriptor = new ServiceDescriptor(typeof(IMessageTransport), factory, lifetime);
        services.Add(descriptor);

        return configurator;
    }
}
