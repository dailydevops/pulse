namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring PostgreSQL outbox services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class PostgreSqlExtensions
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
    /// <item><description><see cref="IEventOutbox"/> as <see cref="PostgreSqlEventOutbox"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="PostgreSqlOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="PostgreSqlOutboxManagement"/> (Scoped)</description></item>
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

        return configurator.AddPostgreSqlOutbox(opts =>
        {
            opts.ConnectionString = connectionString;
            configureOptions?.Invoke(opts);
        });
    }

    /// <summary>
    /// Adds PostgreSQL outbox persistence with a connection string provider factory.
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
    /// <item><description><see cref="IEventOutbox"/> as <see cref="PostgreSqlEventOutbox"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="PostgreSqlOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="PostgreSqlOutboxManagement"/> (Scoped)</description></item>
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

        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        _ = services.AddSingleton<IConfigureOptions<OutboxOptions>>(sp => new ConfigureOptions<OutboxOptions>(o =>
            o.ConnectionString = connectionStringFactory(sp)
        ));

        return configurator.RegisterPostgreSqlOutboxServices();
    }

    /// <summary>
    /// Adds PostgreSQL outbox persistence using ADO.NET with a full options configuration action.
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
    /// <item><description><see cref="IEventOutbox"/> as <see cref="PostgreSqlEventOutbox"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="PostgreSqlOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="PostgreSqlOutboxManagement"/> (Scoped)</description></item>
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
    ///     .AddPostgreSqlOutbox(opts =>
    ///     {
    ///         opts.ConnectionString = "Host=localhost;Database=MyDb;Username=postgres;Password=secret;";
    ///         opts.Schema = "myschema";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddPostgreSqlOutbox(
        this IMediatorBuilder configurator,
        Action<OutboxOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterPostgreSqlOutboxServices();
    }

    /// <summary>
    /// Registers a unit-of-work type as <see cref="IOutboxTransactionScope"/> (Scoped) so that
    /// <see cref="PostgreSqlEventOutbox"/> can enlist in the caller's transaction automatically.
    /// </summary>
    /// <typeparam name="TUnitOfWork">
    /// A type that implements both the application unit-of-work contract and <see cref="IOutboxTransactionScope"/>.
    /// </typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Call this method after <see cref="AddPostgreSqlOutbox(IMediatorBuilder, string, Action{OutboxOptions}?)"/>
    /// to wire up your unit-of-work so that <see cref="PostgreSqlEventOutbox"/> automatically
    /// enlists in any active <see cref="Npgsql.NpgsqlTransaction"/> owned by the unit-of-work.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddOutbox()
    ///     .AddPostgreSqlOutbox("Host=localhost;Database=MyDb;Username=postgres;Password=secret;")
    ///     .AddPostgreSqlOutboxTransactionScope&lt;MyUnitOfWork&gt;()
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddPostgreSqlOutboxTransactionScope<TUnitOfWork>(this IMediatorBuilder configurator)
        where TUnitOfWork : class, IOutboxTransactionScope
    {
        ArgumentNullException.ThrowIfNull(configurator);

        _ = configurator.Services.AddScoped<IOutboxTransactionScope, TUnitOfWork>();

        return configurator;
    }

    private static IMediatorBuilder RegisterPostgreSqlOutboxServices(this IMediatorBuilder configurator)
    {
        // AddOutbox() uses TryAdd* internally, so this call is safe even when AddOutbox() was already invoked.
        _ = configurator
            .AddOutbox()
            .Services.RemoveAll<IEventOutbox>()
            .AddScoped<IEventOutbox, PostgreSqlEventOutbox>()
            .RemoveAll<IOutboxRepository>()
            .AddScoped<IOutboxRepository, PostgreSqlOutboxRepository>()
            .RemoveAll<IOutboxManagement>()
            .AddScoped<IOutboxManagement, PostgreSqlOutboxManagement>();

        return configurator;
    }
}
