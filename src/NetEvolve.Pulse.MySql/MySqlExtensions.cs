namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring MySQL outbox services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class MySqlExtensions
{
    /// <summary>
    /// Adds MySQL outbox persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionString">The MySQL connection string (e.g., <c>"Server=localhost;Database=mydb;User Id=root;Password=secret;"</c>).</param>
    /// <param name="configureOptions">Optional action to further configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/OutboxMessage.sql</c> against the target MySQL
    /// database to create the required table and indexes before using this provider.
    /// MySQL 8.0 or later is required for <c>SELECT … FOR UPDATE SKIP LOCKED</c> support.
    /// <para><strong>Schema:</strong></para>
    /// MySQL does not use schema namespaces. The <see cref="OutboxOptions.Schema"/> property is
    /// ignored; tables are always created in the active database from the connection string.
    /// <para><strong>Interoperability:</strong></para>
    /// Uses <c>BINARY(16)</c> for <see cref="Guid"/> and <c>BIGINT</c> (UTC ticks) for
    /// <see cref="DateTimeOffset"/>, matching the Entity Framework MySQL provider schema.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IEventOutbox"/> as <see cref="OutboxEventStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="MySqlOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="MySqlOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core outbox services are registered automatically; calling
    /// <see cref="OutboxExtensions.AddOutbox"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddMySqlOutbox("Server=localhost;Database=mydb;User Id=root;Password=secret;")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddMySqlOutbox(
        this IMediatorBuilder configurator,
        string connectionString,
        Action<OutboxOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return configurator.AddMySqlOutbox(opts =>
        {
            opts.ConnectionString = connectionString;
            configureOptions?.Invoke(opts);
        });
    }

    /// <summary>
    /// Adds MySQL outbox persistence with a connection string provider factory.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionStringFactory">Factory function to resolve the connection string from the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureOptions">Optional action to configure additional <see cref="OutboxOptions"/> settings.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="connectionStringFactory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Use this overload when the connection string needs to be resolved from configuration
    /// or other services at runtime.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddMySqlOutbox(
    ///         sp => sp.GetRequiredService&lt;IConfiguration&gt;().GetConnectionString("Outbox")!,
    ///         options => options.TableName = "MyOutboxMessages")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddMySqlOutbox(
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

        return configurator.RegisterMySqlOutboxServices();
    }

    /// <summary>
    /// Adds MySQL outbox persistence with a full options configuration action.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddMySqlOutbox(opts =>
    ///     {
    ///         opts.ConnectionString = "Server=localhost;Database=mydb;User Id=root;Password=secret;";
    ///         opts.TableName = "OutboxMessage";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddMySqlOutbox(
        this IMediatorBuilder configurator,
        Action<OutboxOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterMySqlOutboxServices();
    }

    /// <summary>
    /// Registers a unit-of-work type as <see cref="IOutboxTransactionScope"/> (Scoped) so that
    /// <see cref="OutboxEventStore"/> can enlist in the caller's MySQL transaction automatically.
    /// </summary>
    /// <typeparam name="TUnitOfWork">
    /// A type that implements both the application unit-of-work contract and <see cref="IOutboxTransactionScope"/>.
    /// </typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Call this method after <see cref="AddMySqlOutbox(IMediatorBuilder, string, Action{OutboxOptions}?)"/>
    /// to wire up your unit-of-work so that <see cref="OutboxEventStore"/> automatically
    /// enlists in any active <see cref="MySql.Data.MySqlClient.MySqlTransaction"/> owned by the unit-of-work.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddMySqlOutbox("Server=localhost;Database=mydb;User Id=root;Password=secret;")
    ///     .AddMySqlOutboxTransactionScope&lt;MyUnitOfWork&gt;()
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddMySqlOutboxTransactionScope<TUnitOfWork>(this IMediatorBuilder configurator)
        where TUnitOfWork : class, IOutboxTransactionScope
    {
        ArgumentNullException.ThrowIfNull(configurator);

        _ = configurator.Services.AddScoped<IOutboxTransactionScope, TUnitOfWork>();

        return configurator;
    }

    private static IMediatorBuilder RegisterMySqlOutboxServices(this IMediatorBuilder configurator)
    {
        // AddOutbox() uses TryAdd* internally, so this call is safe even when AddOutbox() was already invoked.
        _ = configurator
            .AddOutbox()
            .Services.RemoveAll<IOutboxRepository>()
            .AddScoped<IOutboxRepository, MySqlOutboxRepository>()
            .RemoveAll<IOutboxManagement>()
            .AddScoped<IOutboxManagement, MySqlOutboxManagement>();

        return configurator;
    }
}
