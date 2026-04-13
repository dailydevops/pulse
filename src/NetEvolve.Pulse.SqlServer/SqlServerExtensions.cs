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
    /// <item><description><see cref="IEventOutbox"/> as <see cref="SqlServerEventOutbox"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SqlServerOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SqlServerOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core outbox services are registered automatically; calling
    /// <see cref="OutboxExtensions.AddOutbox"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
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
    /// <item><description><see cref="IEventOutbox"/> as <see cref="SqlServerEventOutbox"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SqlServerOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SqlServerOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core outbox services are registered automatically; calling
    /// <see cref="OutboxExtensions.AddOutbox"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
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
    /// <item><description><see cref="IEventOutbox"/> as <see cref="SqlServerEventOutbox"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SqlServerOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SqlServerOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core outbox services are registered automatically; calling
    /// <see cref="OutboxExtensions.AddOutbox"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
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

    /// <summary>
    /// Registers a unit-of-work type as <see cref="IOutboxTransactionScope"/> (Scoped) so that
    /// <see cref="SqlServerEventOutbox"/> can enlist in the caller's transaction automatically.
    /// </summary>
    /// <typeparam name="TUnitOfWork">
    /// A type that implements both the application unit-of-work contract and <see cref="IOutboxTransactionScope"/>.
    /// </typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Call this method after <see cref="AddSqlServerOutbox(IMediatorBuilder, string, Action{OutboxOptions}?)"/>
    /// to wire up your unit-of-work so that <see cref="SqlServerEventOutbox"/> automatically
    /// enlists in any active <see cref="Microsoft.Data.SqlClient.SqlTransaction"/> owned by the unit-of-work.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddOutbox()
    ///     .AddSqlServerOutbox("Server=.;Database=MyDb;Integrated Security=true;")
    ///     .AddSqlServerOutboxTransactionScope&lt;MyUnitOfWork&gt;()
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSqlServerOutboxTransactionScope<TUnitOfWork>(this IMediatorBuilder configurator)
        where TUnitOfWork : class, IOutboxTransactionScope
    {
        ArgumentNullException.ThrowIfNull(configurator);

        _ = configurator.Services.AddScoped<IOutboxTransactionScope, TUnitOfWork>();

        return configurator;
    }

    private static IMediatorBuilder RegisterSqlServerOutboxServices(this IMediatorBuilder configurator)
    {
        // Register core outbox infrastructure (OutboxEventHandler, processor, etc.)
        // Uses TryAdd* so it is safe to call even when AddOutbox() was already called.
        _ = configurator.AddOutbox();

        var services = configurator.Services;

        _ = services.RemoveAll<IEventOutbox>();
        _ = services
            .AddScoped<IEventOutbox, SqlServerEventOutbox>()
            .AddScoped<IOutboxRepository, SqlServerOutboxRepository>()
            .AddScoped<IOutboxManagement, SqlServerOutboxManagement>();

        return configurator;
    }
}
