namespace NetEvolve.Pulse;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring Entity Framework outbox services on <see cref="IMediatorConfigurator"/>.
/// </summary>
public static class EntityFrameworkMediatorConfiguratorExtensions
{
    /// <summary>
    /// Adds Entity Framework Core outbox persistence with the specified DbContext.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <list type="number">
    /// <item><description>Your DbContext must implement <see cref="IOutboxDbContext"/></description></item>
    /// <item><description>Apply <see cref="OutboxMessageConfiguration"/> in OnModelCreating</description></item>
    /// <item><description>Generate and apply migrations with your chosen provider</description></item>
    /// </list>
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="EntityFrameworkOutboxRepository{TContext}"/> (Scoped)</description></item>
    /// <item><description><see cref="IEventOutbox"/> as <see cref="EntityFrameworkEventOutbox{TContext}"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxTransactionScope"/> as <see cref="EntityFrameworkOutboxTransactionScope{TContext}"/> (Scoped)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// The DbContext must already be registered in the service collection.
    /// This method does not register the DbContext itself.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register DbContext with your chosen provider
    /// services.AddDbContext&lt;MyDbContext&gt;(options =&gt;
    ///     options.UseSqlServer(connectionString));
    ///
    /// // Add outbox support
    /// services.AddPulse(config =&gt; config
    ///     .AddOutbox()
    ///     .AddEntityFrameworkOutbox&lt;MyDbContext&gt;()
    /// );
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddEntityFrameworkOutbox<TContext>(
        this IMediatorConfigurator configurator,
        Action<OutboxOptions>? configureOptions = null
    )
        where TContext : DbContext, IOutboxDbContext
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        _ = services.AddOptions<OutboxOptions>();

        // Register options if configureOptions is provided
        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        // Ensure TimeProvider is registered
        services.TryAddSingleton(TimeProvider.System);

        // Register the repository
        _ = services.AddScoped<IOutboxRepository, EntityFrameworkOutboxRepository<TContext>>();

        // Register the event outbox (overrides the default OutboxEventStore)
        _ = services.AddScoped<IEventOutbox, EntityFrameworkEventOutbox<TContext>>();

        // Register the transaction scope
        _ = services.AddScoped<IOutboxTransactionScope, EntityFrameworkOutboxTransactionScope<TContext>>();

        return configurator;
    }
}
