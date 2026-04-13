namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring SQLite outbox services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class SQLiteExtensions
{
    /// <summary>
    /// Adds SQLite outbox persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionString">The SQLite connection string (e.g., <c>"Data Source=outbox.db"</c>).</param>
    /// <param name="configureOptions">Optional action to further configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/001_CreateOutboxTable.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IEventOutbox"/> as <see cref="OutboxEventStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SQLiteOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SQLiteOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core outbox services are registered automatically; calling
    /// <see cref="OutboxExtensions.AddOutbox"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSQLiteOutbox("Data Source=outbox.db")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSQLiteOutbox(
        this IMediatorBuilder configurator,
        string connectionString,
        Action<OutboxOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return configurator.AddSQLiteOutbox(opts =>
        {
            opts.ConnectionString = connectionString;
            configureOptions?.Invoke(opts);
        });
    }

    /// <summary>
    /// Adds SQLite outbox persistence with a connection string provider factory.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionStringFactory">Factory function to resolve the connection string from the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureOptions">Optional action to configure additional <see cref="OutboxOptions"/> settings.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Use this overload when the connection string needs to be resolved from configuration
    /// or other services at runtime.
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/001_CreateOutboxTable.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IEventOutbox"/> as <see cref="OutboxEventStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SQLiteOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SQLiteOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core outbox services are registered automatically; calling
    /// <see cref="OutboxExtensions.AddOutbox"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSQLiteOutbox(
    ///         sp => sp.GetRequiredService&lt;IConfiguration&gt;().GetConnectionString("Outbox")!,
    ///         options => options.EnableWalMode = false)
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSQLiteOutbox(
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

        return configurator.RegisterSQLiteOutboxServices();
    }

    /// <summary>
    /// Adds SQLite outbox persistence using ADO.NET with a full options configuration action.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/001_CreateOutboxTable.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IEventOutbox"/> as <see cref="OutboxEventStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SQLiteOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SQLiteOutboxManagement"/> (Scoped)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core outbox services are registered automatically; calling
    /// <see cref="OutboxExtensions.AddOutbox"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSQLiteOutbox(opts =>
    ///     {
    ///         opts.ConnectionString = "Data Source=outbox.db";
    ///         opts.EnableWalMode = true;
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSQLiteOutbox(
        this IMediatorBuilder configurator,
        Action<OutboxOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterSQLiteOutboxServices();
    }

    /// <summary>
    /// Registers a unit-of-work type as <see cref="IOutboxTransactionScope"/> (Scoped) so that
    /// <see cref="OutboxEventStore"/> can enlist in the caller's transaction automatically.
    /// </summary>
    /// <typeparam name="TUnitOfWork">
    /// A type that implements both the application unit-of-work contract and <see cref="IOutboxTransactionScope"/>.
    /// </typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// Call this method after <see cref="AddSQLiteOutbox(IMediatorBuilder, string, Action{OutboxOptions}?)"/>
    /// to wire up your unit-of-work so that <see cref="OutboxEventStore"/> automatically
    /// enlists in any active <see cref="Microsoft.Data.Sqlite.SqliteTransaction"/> owned by the unit-of-work.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSQLiteOutbox("Data Source=outbox.db")
    ///     .AddSQLiteOutboxTransactionScope&lt;MyUnitOfWork&gt;()
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSQLiteOutboxTransactionScope<TUnitOfWork>(this IMediatorBuilder configurator)
        where TUnitOfWork : class, IOutboxTransactionScope
    {
        ArgumentNullException.ThrowIfNull(configurator);

        _ = configurator.Services.AddScoped<IOutboxTransactionScope, TUnitOfWork>();

        return configurator;
    }

    /// <summary>
    /// Adds SQLite outbox persistence using ADO.NET with a connection string shorthand.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionString">The SQLite connection string (e.g., <c>"Data Source=outbox.db"</c>).</param>
    /// <param name="configureOptions">Optional action to further configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/001_CreateOutboxTable.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SQLiteOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SQLiteOutboxManagement"/> (Scoped)</description></item>
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
    ///     .UseSQLiteOutbox("Data Source=outbox.db")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder UseSQLiteOutbox(
        this IMediatorBuilder configurator,
        string connectionString,
        Action<OutboxOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return configurator.UseSQLiteOutbox(opts =>
        {
            opts.ConnectionString = connectionString;
            configureOptions?.Invoke(opts);
        });
    }

    /// <summary>
    /// Adds SQLite outbox persistence using ADO.NET with a full options configuration action.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="OutboxOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/001_CreateOutboxTable.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IOutboxRepository"/> as <see cref="SQLiteOutboxRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IOutboxManagement"/> as <see cref="SQLiteOutboxManagement"/> (Scoped)</description></item>
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
    ///     .UseSQLiteOutbox(opts =>
    ///     {
    ///         opts.ConnectionString = "Data Source=outbox.db";
    ///         opts.EnableWalMode = true;
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder UseSQLiteOutbox(
        this IMediatorBuilder configurator,
        Action<OutboxOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var services = configurator.Services;

        _ = services.Configure(configureOptions);

        // Ensure TimeProvider is registered
        services.TryAddSingleton(TimeProvider.System);

        // Register the repository
        _ = services.AddScoped<IOutboxRepository>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OutboxOptions>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var transactionScope = sp.GetService<IOutboxTransactionScope>();

            return new SQLiteOutboxRepository(options, timeProvider, transactionScope);
        });

        // Register the management API
        _ = services.AddScoped<IOutboxManagement>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OutboxOptions>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();

            return new SQLiteOutboxManagement(options, timeProvider);
        });

        return configurator;
    }

    private static IMediatorBuilder RegisterSQLiteOutboxServices(this IMediatorBuilder configurator)
    {
        // AddOutbox() uses TryAdd* internally, so this call is safe even when AddOutbox() was already invoked.
        _ = configurator
            .AddOutbox()
            .Services.RemoveAll<IOutboxRepository>()
            .AddScoped<IOutboxRepository, SQLiteOutboxRepository>()
            .RemoveAll<IOutboxManagement>()
            .AddScoped<IOutboxManagement, SQLiteOutboxManagement>();

        return configurator;
    }
}
