namespace NetEvolve.Pulse;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Extension methods for configuring the Entity Framework Core command dead letter store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class EntityFrameworkCommandDeadLetterExtensions
{
    /// <summary>
    /// Adds Entity Framework Core-backed command dead letter persistence with the specified DbContext.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements <see cref="ICommandDeadLetterDbContext"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="CommandDeadLetterOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <list type="number">
    /// <item><description>Your DbContext must implement <see cref="ICommandDeadLetterDbContext"/></description></item>
    /// <item><description>Apply <see cref="ModelBuilderExtensions.ApplyPulseConfiguration{TContext}(ModelBuilder, TContext)"/> in OnModelCreating</description></item>
    /// <item><description>Generate and apply migrations with your chosen provider</description></item>
    /// </list>
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="ICommandDeadLetterStore"/> as <see cref="EntityFrameworkCommandDeadLetterStore{TContext}"/> (Scoped)</description></item>
    /// <item><description><see cref="ICommandDeadLetterManagement"/> as <see cref="EntityFrameworkCommandDeadLetterManagement{TContext}"/> (Scoped)</description></item>
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
    /// // Add command dead letter support
    /// services.AddPulse(config =&gt; config
    ///     .AddEntityFrameworkCommandDeadLetterStore&lt;MyDbContext&gt;()
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddEntityFrameworkCommandDeadLetterStore<TContext>(
        this IMediatorBuilder configurator,
        Action<CommandDeadLetterOptions>? configureOptions = null
    )
        where TContext : DbContext, ICommandDeadLetterDbContext
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        _ = services.Configure(configureOptions ?? (_ => { }));

        _ = services
            .RemoveAll<ICommandDeadLetterStore>()
            .AddScoped<ICommandDeadLetterStore, EntityFrameworkCommandDeadLetterStore<TContext>>();

        _ = services
            .RemoveAll<ICommandDeadLetterManagement>()
            .AddScoped<ICommandDeadLetterManagement, EntityFrameworkCommandDeadLetterManagement<TContext>>();

        return configurator;
    }
}
