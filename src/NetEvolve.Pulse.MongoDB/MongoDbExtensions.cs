namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring MongoDB outbox services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class MongoDbExtensions
{
    /// <summary>
    /// Adds MongoDB outbox persistence, registering core outbox services and the
    /// <see cref="MongoDbOutboxRepository"/> as the <see cref="IOutboxRepository"/> implementation.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="MongoDbOutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Register <see cref="IMongoClient"/> in the dependency injection container before calling this method.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IEventOutbox"/> as <see cref="OutboxEventStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="MongoDbOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core outbox services are registered automatically; calling
    /// <see cref="OutboxExtensions.AddOutbox"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;IMongoClient&gt;(new MongoClient(connectionString));
    /// services.AddPulse(config => config
    ///     .AddMongoDbOutbox(opts =>
    ///     {
    ///         opts.DatabaseName = "mydb";
    ///         opts.CollectionName = "outbox_messages";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddMongoDbOutbox(
        this IMediatorBuilder configurator,
        Action<MongoDbOutboxOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterMongoDbOutboxServices();
    }

    /// <summary>
    /// Adds MongoDB outbox persistence as the <see cref="IOutboxRepository"/> implementation.
    /// Call <see cref="OutboxExtensions.AddOutbox"/> first to register core outbox services.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="MongoDbOutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Register <see cref="IMongoClient"/> in the dependency injection container before calling this method.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="MongoDbOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Call <see cref="OutboxExtensions.AddOutbox"/> first to register core outbox services
    /// before calling this method.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;IMongoClient&gt;(new MongoClient(connectionString));
    /// services.AddPulse(config => config
    ///     .AddOutbox()
    ///     .UseMongoDbOutbox(opts =>
    ///     {
    ///         opts.DatabaseName = "mydb";
    ///         opts.CollectionName = "outbox_messages";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder UseMongoDbOutbox(
        this IMediatorBuilder configurator,
        Action<MongoDbOutboxOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var services = configurator.Services;

        _ = services.Configure(configureOptions);

        // Ensure TimeProvider is registered
        services.TryAddSingleton(TimeProvider.System);

        // Register the repository using IMongoClient from DI
        _ = services.AddScoped<IOutboxRepository>(sp =>
        {
            var mongoClient = sp.GetRequiredService<IMongoClient>();
            var options = sp.GetRequiredService<IOptions<MongoDbOutboxOptions>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();

            return new MongoDbOutboxRepository(mongoClient, options, timeProvider);
        });

        return configurator;
    }

    private static IMediatorBuilder RegisterMongoDbOutboxServices(this IMediatorBuilder configurator)
    {
        // AddOutbox() uses TryAdd* internally, so this call is safe even when AddOutbox() was already invoked.
        _ = configurator
            .AddOutbox()
            .Services.RemoveAll<IOutboxRepository>()
            .AddScoped<IOutboxRepository>(sp =>
            {
                var mongoClient = sp.GetRequiredService<IMongoClient>();
                var options = sp.GetRequiredService<IOptions<MongoDbOutboxOptions>>();
                var timeProvider = sp.GetRequiredService<TimeProvider>();

                return new MongoDbOutboxRepository(mongoClient, options, timeProvider);
            });

        return configurator;
    }
}
