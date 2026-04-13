namespace NetEvolve.Pulse;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Extensibility.Outbox;
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

        var providerName = context.Database.ProviderName;

        if (context is IOutboxDbContext)
        {
            var configuration = GetOutboxConfiguration(context, providerName);

            _ = modelBuilder.ApplyConfiguration(configuration);
        }

        return modelBuilder;
    }

    /// <summary>
    /// Selects and instantiates the provider-appropriate <see cref="IEntityTypeConfiguration{TEntity}"/>
    /// for <see cref="OutboxMessage"/> based on the active EF Core provider.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type, used to resolve registered <see cref="OutboxOptions"/>.</typeparam>
    /// <param name="context">The DbContext instance used to resolve <see cref="IOptions{TOptions}"/> of <see cref="OutboxOptions"/>.</param>
    /// <param name="providerName">The EF Core provider name read from <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.ProviderName"/>.</param>
    /// <returns>A provider-specific <see cref="IEntityTypeConfiguration{TEntity}"/> for <see cref="OutboxMessage"/>.</returns>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="providerName"/> is not a supported EF Core provider.</exception>
    private static IEntityTypeConfiguration<OutboxMessage> GetOutboxConfiguration<TContext>(
        TContext context,
        string? providerName
    )
        where TContext : DbContext
    {
        IOptions<OutboxOptions>? resolvedOptions = null;
        try
        {
            // EF Core's GetService throws InvalidOperationException when the service is not
            // registered (instead of returning null), so we catch and fall back to defaults.
            resolvedOptions = context.GetService<IOptions<OutboxOptions>>();
        }
        catch (InvalidOperationException)
        {
            // IOptions<OutboxOptions> not registered; use default options.
        }

        resolvedOptions ??= Options.Create(new OutboxOptions());
        return providerName switch
        {
            ProviderName.Npgsql => new PostgreSqlOutboxMessageConfiguration(resolvedOptions),
            ProviderName.Sqlite => new SqliteOutboxMessageConfiguration(resolvedOptions),
            ProviderName.SqlServer => new SqlServerOutboxMessageConfiguration(resolvedOptions),
            ProviderName.PomeloMySql or ProviderName.OracleMySql => new MySqlOutboxMessageConfiguration(
                resolvedOptions
            ),
            ProviderName.InMemory => new InMemoryOutboxMessageConfiguration(resolvedOptions),
            _ => throw new NotSupportedException($"Unsupported EF Core provider: {providerName}"),
        };
    }
}
