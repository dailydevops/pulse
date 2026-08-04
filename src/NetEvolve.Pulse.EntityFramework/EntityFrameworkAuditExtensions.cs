namespace NetEvolve.Pulse;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Extension methods for configuring the Entity Framework Core audit trail store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class EntityFrameworkAuditExtensions
{
    /// <summary>
    /// Adds Entity Framework Core-backed audit trail persistence with the specified DbContext.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements <see cref="IAuditStoreDbContext"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="AuditStoreOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <list type="number">
    /// <item><description>Your DbContext must implement <see cref="IAuditStoreDbContext"/></description></item>
    /// <item><description>Apply <see cref="ModelBuilderExtensions.ApplyPulseConfiguration{TContext}(ModelBuilder, TContext)"/> in OnModelCreating</description></item>
    /// <item><description>Generate and apply migrations with your chosen provider</description></item>
    /// </list>
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IAuditStore"/> as <see cref="EntityFrameworkAuditStore{TContext}"/> (Scoped)</description></item>
    /// <item><description><see cref="IAuditManagement"/> as <see cref="EntityFrameworkAuditManagement{TContext}"/> (Scoped)</description></item>
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
    /// // Add audit trail support
    /// services.AddPulse(config =&gt; config
    ///     .AddEntityFrameworkAuditStore&lt;MyDbContext&gt;()
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddEntityFrameworkAuditStore<TContext>(
        this IMediatorBuilder configurator,
        Action<AuditStoreOptions>? configureOptions = null
    )
        where TContext : DbContext, IAuditStoreDbContext
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        _ = services.Configure(configureOptions ?? (_ => { }));

        _ = services.RemoveAll<IAuditStore>().AddScoped<IAuditStore, EntityFrameworkAuditStore<TContext>>();

        _ = services
            .RemoveAll<IAuditManagement>()
            .AddScoped<IAuditManagement, EntityFrameworkAuditManagement<TContext>>();

        return configurator;
    }
}
