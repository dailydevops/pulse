namespace NetEvolve.Pulse;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Extension methods for configuring the Entity Framework Core idempotency store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class EntityFrameworkIdempotencyExtensions
{
    /// <summary>
    /// Adds Entity Framework Core-backed idempotency key persistence with the specified DbContext.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements <see cref="IIdempotencyStoreDbContext"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="IdempotencyKeyOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <list type="number">
    /// <item><description>Your DbContext must implement <see cref="IIdempotencyStoreDbContext"/></description></item>
    /// <item><description>Apply <see cref="ModelBuilderExtensions.ApplyPulseConfiguration{TContext}(ModelBuilder, TContext)"/> in OnModelCreating</description></item>
    /// <item><description>Generate and apply migrations with your chosen provider</description></item>
    /// </list>
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IIdempotencyKeyRepository"/> as <see cref="EntityFrameworkIdempotencyKeyRepository{TContext}"/> (Scoped)</description></item>
    /// <item><description><see cref="IIdempotencyStore"/> as <see cref="IdempotencyStore"/> (Scoped, via <see cref="IdempotencyExtensions.AddIdempotency"/>)</description></item>
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
    /// // Add idempotency support
    /// services.AddPulse(config =&gt; config
    ///     .AddIdempotency()
    ///     .AddEntityFrameworkIdempotencyStore&lt;MyDbContext&gt;()
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddEntityFrameworkIdempotencyStore<TContext>(
        this IMediatorBuilder configurator,
        Action<IdempotencyKeyOptions>? configureOptions = null
    )
        where TContext : DbContext, IIdempotencyStoreDbContext
    {
        ArgumentNullException.ThrowIfNull(configurator);

        _ = configurator.AddIdempotency();

        var services = configurator.Services;

        _ = services.Configure(configureOptions ?? (_ => { }));

        services.TryAddSingleton(TimeProvider.System);

        _ = services
            .RemoveAll<IIdempotencyKeyRepository>()
            .AddScoped<IIdempotencyKeyRepository, EntityFrameworkIdempotencyKeyRepository<TContext>>();

        return configurator;
    }
}
