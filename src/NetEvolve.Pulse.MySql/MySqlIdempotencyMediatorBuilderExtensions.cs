namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Extension methods for configuring MySQL idempotency store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class MySqlIdempotencyMediatorBuilderExtensions
{
    /// <summary>
    /// Adds MySQL idempotency key persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionString">The MySQL connection string.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="IdempotencyKeyOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/IdempotencyKey.sql</c> against the target MySQL
    /// database to create the required table before using this provider.
    /// <para><strong>Schema:</strong></para>
    /// MySQL does not use schema namespaces. The <see cref="IdempotencyKeyOptions.Schema"/> property is
    /// ignored; tables are always created in the active database from the connection string.
    /// <para><strong>Interoperability:</strong></para>
    /// Stores <see cref="DateTimeOffset"/> values as <c>BIGINT</c> (UTC ticks), matching
    /// the Entity Framework MySQL provider schema.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IIdempotencyKeyRepository"/> as <see cref="MySqlIdempotencyKeyRepository"/> (Scoped)</description></item>
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
    ///     .AddMySqlIdempotencyStore("Server=localhost;Database=mydb;User Id=root;Password=secret;")
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddMySqlIdempotencyStore(
        this IMediatorBuilder configurator,
        string connectionString,
        Action<IdempotencyKeyOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return configurator.AddMySqlIdempotencyStore(opts =>
        {
            opts.ConnectionString = connectionString;
            configureOptions?.Invoke(opts);
        });
    }

    /// <summary>
    /// Adds MySQL idempotency key persistence with a connection string provider factory.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionStringFactory">Factory function to resolve the connection string from the <see cref="IServiceProvider"/>.</param>
    /// <param name="configureOptions">Optional action to configure additional <see cref="IdempotencyKeyOptions"/> settings.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="connectionStringFactory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Use this overload when the connection string needs to be resolved from configuration
    /// or other services at runtime.
    /// </remarks>
    public static IMediatorBuilder AddMySqlIdempotencyStore(
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

        return configurator.RegisterMySqlIdempotencyServices();
    }

    /// <summary>
    /// Adds MySQL idempotency key persistence with a full options configuration action.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="IdempotencyKeyOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder AddMySqlIdempotencyStore(
        this IMediatorBuilder configurator,
        Action<IdempotencyKeyOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterMySqlIdempotencyServices();
    }

    private static IMediatorBuilder RegisterMySqlIdempotencyServices(this IMediatorBuilder configurator)
    {
        _ = configurator
            .AddIdempotency()
            .Services.RemoveAll<IIdempotencyKeyRepository>()
            .AddScoped<IIdempotencyKeyRepository, MySqlIdempotencyKeyRepository>();

        return configurator;
    }
}
