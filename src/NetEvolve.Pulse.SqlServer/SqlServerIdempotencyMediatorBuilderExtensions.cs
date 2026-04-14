namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Extension methods for configuring SQL Server idempotency store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class SqlServerIdempotencyMediatorBuilderExtensions
{
    /// <summary>
    /// Adds SQL Server idempotency key persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="IdempotencyKeyOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/IdempotencyKey.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IIdempotencyKeyRepository"/> as <see cref="SqlServerIdempotencyKeyRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IIdempotencyStore"/> as <see cref="IdempotencyStore"/> (Scoped, via <see cref="IdempotencyExtensions.AddIdempotency"/>)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core idempotency services are registered automatically; calling
    /// <see cref="IdempotencyExtensions.AddIdempotency"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSqlServerIdempotencyStore("Server=.;Database=MyDb;Integrated Security=true;")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSqlServerIdempotencyStore(
        this IMediatorBuilder configurator,
        string connectionString,
        Action<IdempotencyKeyOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return configurator.AddSqlServerIdempotencyStore(opts =>
        {
            opts.ConnectionString = connectionString;
            configureOptions?.Invoke(opts);
        });
    }

    /// <summary>
    /// Adds SQL Server idempotency key persistence with a connection string provider factory.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionStringFactory">Factory function to resolve the connection string from the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureOptions">Optional action to configure additional <see cref="IdempotencyKeyOptions"/> settings.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="connectionStringFactory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Use this overload when the connection string needs to be resolved from configuration
    /// or other services at runtime.
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/IdempotencyKey.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IIdempotencyKeyRepository"/> as <see cref="SqlServerIdempotencyKeyRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IIdempotencyStore"/> as <see cref="IdempotencyStore"/> (Scoped, via <see cref="IdempotencyExtensions.AddIdempotency"/>)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core idempotency services are registered automatically; calling
    /// <see cref="IdempotencyExtensions.AddIdempotency"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSqlServerIdempotencyStore(
    ///         sp => sp.GetRequiredService&lt;IConfiguration&gt;().GetConnectionString("Idempotency")!,
    ///         options => options.Schema = "myschema")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSqlServerIdempotencyStore(
        this IMediatorBuilder configurator,
        Func<IServiceProvider, string> connectionStringFactory,
        Action<IdempotencyKeyOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(connectionStringFactory);

        var services = configurator.Services;

        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        _ = services.AddSingleton<IConfigureOptions<IdempotencyKeyOptions>>(
            sp => new ConfigureOptions<IdempotencyKeyOptions>(o => o.ConnectionString = connectionStringFactory(sp))
        );

        return configurator.RegisterSqlServerIdempotencyStore();
    }

    /// <summary>
    /// Adds SQL Server idempotency key persistence using ADO.NET with a full options configuration action.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="IdempotencyKeyOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/IdempotencyKey.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IIdempotencyKeyRepository"/> as <see cref="SqlServerIdempotencyKeyRepository"/> (Scoped)</description></item>
    /// <item><description><see cref="IIdempotencyStore"/> as <see cref="IdempotencyStore"/> (Scoped, via <see cref="IdempotencyExtensions.AddIdempotency"/>)</description></item>
    /// <item><description><see cref="TimeProvider"/> (Singleton, if not already registered)</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core idempotency services are registered automatically; calling
    /// <see cref="IdempotencyExtensions.AddIdempotency"/> before this method is optional but harmless.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSqlServerIdempotencyStore(opts =>
    ///     {
    ///         opts.ConnectionString = "Server=.;Database=MyDb;Integrated Security=true;";
    ///         opts.Schema = "myschema";
    ///         opts.TimeToLive = TimeSpan.FromHours(24);
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSqlServerIdempotencyStore(
        this IMediatorBuilder configurator,
        Action<IdempotencyKeyOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterSqlServerIdempotencyStore();
    }

    private static IMediatorBuilder RegisterSqlServerIdempotencyStore(this IMediatorBuilder configurator)
    {
        // AddIdempotency() uses TryAdd* internally, so this call is safe even when AddIdempotency() was already invoked.
        _ = configurator.AddIdempotency();

        var services = configurator.Services;

        services.TryAddSingleton(TimeProvider.System);

        _ = services
            .RemoveAll<IIdempotencyKeyRepository>()
            .AddScoped<IIdempotencyKeyRepository, SqlServerIdempotencyKeyRepository>();

        return configurator;
    }
}
