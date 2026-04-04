namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring SQL Server outbox services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class SqlServerExtensions
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
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SqlServerOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Call <see cref="OutboxExtensions.AddOutbox"/> first to register core outbox services
    /// before calling this method.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddOutbox()
    ///     .AddSqlServerOutbox("Server=.;Database=MyDb;Integrated Security=true;")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSqlServerOutbox(
        this IMediatorBuilder configurator,
        string connectionString,
        Action<OutboxOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return configurator.AddSqlServerOutbox(opts =>
        {
            opts.ConnectionString = connectionString;
            configureOptions?.Invoke(opts);
        });
    }

    /// <summary>
    /// Adds SQL Server outbox persistence with a connection string provider factory.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionStringFactory">Factory function to resolve the connection string from the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureOptions">Optional action to configure additional <see cref="OutboxOptions"/> settings.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Use this overload when the connection string needs to be resolved from configuration
    /// or other services at runtime.
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/OutboxMessage.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SqlServerOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SqlServerOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Call <see cref="OutboxExtensions.AddOutbox"/> first to register core outbox services
    /// before calling this method.
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
    public static IMediatorBuilder AddSqlServerOutbox(
        this IMediatorBuilder configurator,
        Func<IServiceProvider, string> connectionStringFactory,
        Action<OutboxOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(connectionStringFactory);

        var services = configurator.Services;

        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        _ = services.AddSingleton<IConfigureOptions<OutboxOptions>>(sp => new ConfigureOptions<OutboxOptions>(o =>
            o.ConnectionString = connectionStringFactory(sp)
        ));

        return configurator.RegisterSqlServerOutboxServices();
    }

    /// <summary>
    /// Adds SQL Server outbox persistence using ADO.NET with a full options configuration action.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/OutboxMessage.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SqlServerOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SqlServerOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Call <see cref="OutboxExtensions.AddOutbox"/> first to register core outbox services
    /// before calling this method.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddOutbox()
    ///     .AddSqlServerOutbox(opts =>
    ///     {
    ///         opts.ConnectionString = "Server=.;Database=MyDb;Integrated Security=true;";
    ///         opts.Schema = "myschema";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSqlServerOutbox(
        this IMediatorBuilder configurator,
        Action<OutboxOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterSqlServerOutboxServices();
    }

    private static IMediatorBuilder RegisterSqlServerOutboxServices(this IMediatorBuilder configurator)
    {
        var services = configurator.Services;

        services.TryAddSingleton(TimeProvider.System);

        _ = services
            .AddScoped<IOutboxRepository, SqlServerOutboxRepository>()
            .AddScoped<IOutboxManagement, SqlServerOutboxManagement>();

        return configurator;
    }
}
