namespace NetEvolve.Pulse;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for applying Pulse-specific EF Core model configuration via <see cref="ModelBuilder"/>.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies all Pulse-related entity configurations to the model builder.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type. When it implements <see cref="IOutboxDbContext"/>, the outbox message configuration is applied automatically.</typeparam>
    /// <param name="modelBuilder">The model builder to apply the configuration to.</param>
    /// <param name="context">The DbContext instance used to resolve provider-specific configuration.</param>
    /// <returns>The <paramref name="modelBuilder"/> for chaining.</returns>
    public static ModelBuilder ApplyPulseConfiguration<TContext>(this ModelBuilder modelBuilder, TContext context)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        if (context is IOutboxDbContext)
        {
            _ = modelBuilder.ApplyConfiguration(OutboxMessageConfigurationFactory.Create(context));
        }

        return modelBuilder;
    }
}
