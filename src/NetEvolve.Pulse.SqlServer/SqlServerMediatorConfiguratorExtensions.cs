namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring SQL Server outbox services on <see cref="IMediatorConfigurator"/>.
/// </summary>
public static class SqlServerMediatorConfiguratorExtensions
{
    /// <summary>
    /// Adds SQL Server outbox persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/OutboxMessage.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SqlServerOutboxRepository"/> (Scoped)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Call <see cref="OutboxMediatorConfiguratorExtensions.AddOutbox"/> first to register core outbox services,
    /// or this method will register them automatically.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddOutbox()
    ///     .AddSqlServerOutbox("Server=.;Database=MyDb;Integrated Security=true;")
    /// );
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddSqlServerOutbox(
        this IMediatorConfigurator configurator,
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

            return new SqlServerOutboxRepository(connectionString, options, timeProvider, transactionScope);
        });

        return configurator;
    }

    /// <summary>
    /// Adds SQL Server outbox persistence with a connection string provider factory.
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
    ///     .AddSqlServerOutbox(
    ///         sp => sp.GetRequiredService&lt;IConfiguration&gt;().GetConnectionString("Outbox")!,
    ///         options => options.Schema = "myschema")
    /// );
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddSqlServerOutbox(
        this IMediatorConfigurator configurator,
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

            return new SqlServerOutboxRepository(connectionString, options, timeProvider, transactionScope);
        });

        return configurator;
    }
}
