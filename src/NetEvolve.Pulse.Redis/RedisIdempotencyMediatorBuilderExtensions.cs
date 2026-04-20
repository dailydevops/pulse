namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Extension methods for configuring the Redis idempotency store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class RedisIdempotencyMediatorBuilderExtensions
{
    /// <summary>
    /// Adds a Redis-backed idempotency store using atomic <c>SET NX EX</c> operations.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configure">An optional action to configure <see cref="IdempotencyKeyOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <see cref="StackExchange.Redis.IConnectionMultiplexer"/> must be registered in the DI container
    /// by the caller before the application starts.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IIdempotencyKeyRepository"/> as <c>RedisIdempotencyKeyRepository</c> (Scoped)</description></item>
    /// <item><description><see cref="IdempotencyKeyOptions"/> bound from the <c>Pulse:Idempotency:Redis</c> configuration section</description></item>
    /// </list>
    /// <para><strong>Note:</strong></para>
    /// Core idempotency services are registered automatically; calling
    /// <see cref="IdempotencyExtensions.AddIdempotency"/> before this method is optional but harmless.
    /// Options are also bound from the <c>Pulse:Idempotency:Redis</c> configuration section automatically.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register IConnectionMultiplexer first
    /// services.AddSingleton&lt;IConnectionMultiplexer&gt;(_ =>
    ///     ConnectionMultiplexer.Connect("localhost:6379"));
    ///
    /// services.AddPulse(config => config
    ///     .AddRedisIdempotencyStore()
    /// );
    ///
    /// // Or with custom options
    /// services.AddPulse(config => config
    ///     .AddRedisIdempotencyStore(opts =>
    ///     {
    ///         opts.TimeToLive = TimeSpan.FromHours(48);
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddRedisIdempotencyStore(
        this IMediatorBuilder configurator,
        Action<IdempotencyKeyOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        _ = services.AddOptions<IdempotencyKeyOptions>().ValidateOnStart();

        if (configure is not null)
        {
            _ = services.Configure(configure);
        }

        // AddIdempotency() uses TryAdd* internally, so this call is safe even when AddIdempotency() was already invoked.
        _ = configurator.AddIdempotency();

        // Register the Redis repository; the core IdempotencyStore wrapper (registered by AddIdempotency)
        // handles TimeProvider-based TTL, making expiry testable with fake clocks.
        _ = services
            .RemoveAll<IIdempotencyKeyRepository>()
            .AddScoped<IIdempotencyKeyRepository, RedisIdempotencyKeyRepository>();

        return configurator;
    }
}
