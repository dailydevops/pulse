namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring PostgreSQL outbox services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class PostgreSqlMediatorBuilderExtensions
{
    /// <summary>
    /// Adds PostgreSQL outbox persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/OutboxMessage.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="PostgreSqlOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="PostgreSqlOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Call <see cref="OutboxMediatorBuilderExtensions.AddOutbox"/> first to register core outbox services
    /// before calling this method.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddOutbox()
    ///     .AddPostgreSqlOutbox("Host=localhost;Database=MyDb;Username=postgres;Password=secret;")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddPostgreSqlOutbox(
        this IMediatorBuilder configurator,
        string connectionString,
        Action<OutboxOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var services = configurator.Services;

        // Register options if configureOptions is provided
        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        // Ensure TimeProvider is registered
        services.TryAddSingleton(TimeProvider.System);

        // Register the repository
        _ = services.AddScoped<IOutboxRepository>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var transactionScope = sp.GetService<IOutboxTransactionScope>();

            return new PostgreSqlOutboxRepository(connectionString, options, timeProvider, transactionScope);
        });

        // Register the management API
        _ = services.AddScoped<IOutboxManagement>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>();
            return new PostgreSqlOutboxManagement(connectionString, options);
        });

        return configurator;
    }

    /// <summary>
    /// Adds PostgreSQL outbox persistence with a connection string provider factory.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionStringFactory">Factory function to resolve the connection string.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Use this overload when the connection string needs to be resolved from configuration
    /// or other services at runtime.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddOutbox()
    ///     .AddPostgreSqlOutbox(
    ///         sp => sp.GetRequiredService&lt;IConfiguration&gt;().GetConnectionString("Outbox")!,
    ///         options => options.Schema = "myschema")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddPostgreSqlOutbox(
        this IMediatorBuilder configurator,
        Func<IServiceProvider, string> connectionStringFactory,
        Action<OutboxOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(connectionStringFactory);

        var services = configurator.Services;

        // Register options if configureOptions is provided
        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        // Ensure TimeProvider is registered
        services.TryAddSingleton(TimeProvider.System);

        // Register the repository with factory
        _ = services.AddScoped<IOutboxRepository>(sp =>
        {
            var connectionString = connectionStringFactory(sp);
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var transactionScope = sp.GetService<IOutboxTransactionScope>();

            return new PostgreSqlOutboxRepository(connectionString, options, timeProvider, transactionScope);
        });

        // Register the management API with factory
        _ = services.AddScoped<IOutboxManagement>(sp =>
        {
            var connectionString = connectionStringFactory(sp);
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>();
            return new PostgreSqlOutboxManagement(connectionString, options);
        });

        return configurator;
    }
}
