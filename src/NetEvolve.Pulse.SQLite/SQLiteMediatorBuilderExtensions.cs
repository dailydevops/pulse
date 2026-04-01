namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for configuring SQLite outbox services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class SQLiteMediatorBuilderExtensions
{
    /// <summary>
    /// Adds SQLite outbox persistence using ADO.NET with a connection string shorthand.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionString">The SQLite connection string (e.g., <c>"Data Source=outbox.db"</c>).</param>
    /// <param name="configureOptions">Optional action to further configure <see cref="SQLiteOutboxOptions"/>.</param>
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
    /// Call <see cref="OutboxMediatorBuilderExtensions.AddOutbox"/> first to register core outbox services
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
        Action<SQLiteOutboxOptions>? configureOptions = null
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
    /// <param name="configureOptions">Action to configure <see cref="SQLiteOutboxOptions"/>.</param>
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
    /// Call <see cref="OutboxMediatorBuilderExtensions.AddOutbox"/> first to register core outbox services
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
        Action<SQLiteOutboxOptions> configureOptions
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
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SQLiteOutboxOptions>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var transactionScope = sp.GetService<IOutboxTransactionScope>();

            return new SQLiteOutboxRepository(options, timeProvider, transactionScope);
        });

        // Register the management API
        _ = services.AddScoped<IOutboxManagement>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SQLiteOutboxOptions>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();

            return new SQLiteOutboxManagement(options, timeProvider);
        });

        return configurator;
    }
}
