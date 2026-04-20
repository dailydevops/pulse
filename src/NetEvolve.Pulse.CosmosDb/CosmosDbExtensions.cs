namespace NetEvolve.Pulse;

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring Azure Cosmos DB outbox services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class CosmosDbExtensions
{
    /// <summary>
    /// Adds Azure Cosmos DB outbox persistence and registers all core outbox services.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="CosmosDbOutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <list type="number">
    /// <item><description>Register a <see cref="CosmosClient"/> in the DI container before calling this method.</description></item>
    /// <item><description>Create the Cosmos DB database and container before using this provider — automatic schema creation is out of scope.</description></item>
    /// </list>
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IEventOutbox"/> as <see cref="OutboxEventStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="CosmosDbOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="CosmosDbOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core outbox services are registered automatically; calling
    /// <see cref="OutboxExtensions.AddOutbox"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddSingleton(new CosmosClient(connectionString));
    /// services.AddPulse(config => config
    ///     .AddCosmosDbOutbox(opts =>
    ///     {
    ///         opts.DatabaseName = "MyDatabase";
    ///         opts.ContainerName = "outbox_messages";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddCosmosDbOutbox(
        this IMediatorBuilder configurator,
        Action<CosmosDbOutboxOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterCosmosDbOutboxServices();
    }

    /// <summary>
    /// Registers a Cosmos DB-backed <see cref="IOutboxRepository"/> and <see cref="IOutboxManagement"/>
    /// without registering core outbox services.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="CosmosDbOutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <list type="number">
    /// <item><description>Call <see cref="OutboxExtensions.AddOutbox"/> first to register core outbox services.</description></item>
    /// <item><description>Register a <see cref="CosmosClient"/> in the DI container before calling this method.</description></item>
    /// <item><description>Create the Cosmos DB database and container before using this provider.</description></item>
    /// </list>
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="CosmosDbOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="CosmosDbOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddSingleton(new CosmosClient(connectionString));
    /// services.AddPulse(config => config
    ///     .AddOutbox()
    ///     .UseCosmosDbOutbox(opts =>
    ///     {
    ///         opts.DatabaseName = "MyDatabase";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder UseCosmosDbOutbox(
        this IMediatorBuilder configurator,
        Action<CosmosDbOutboxOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var services = configurator.Services;

        _ = services.Configure(configureOptions);

        // Ensure TimeProvider is registered.
        services.TryAddSingleton(TimeProvider.System);

        // Register the repository.
        services.TryAddScoped<IOutboxRepository, CosmosDbOutboxRepository>();

        // Register the management API.
        services.TryAddScoped<IOutboxManagement, CosmosDbOutboxManagement>();

        return configurator;
    }

    private static IMediatorBuilder RegisterCosmosDbOutboxServices(this IMediatorBuilder configurator)
    {
        // AddOutbox() uses TryAdd* internally, so this call is safe even when AddOutbox() was already invoked.
        _ = configurator
            .AddOutbox()
            .Services.RemoveAll<IOutboxRepository>()
            .AddScoped<IOutboxRepository, CosmosDbOutboxRepository>()
            .RemoveAll<IOutboxManagement>()
            .AddScoped<IOutboxManagement, CosmosDbOutboxManagement>();

        return configurator;
    }
}
