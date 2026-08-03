namespace NetEvolve.Pulse;

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for registering the Redis Streams message transport with the Pulse mediator.
/// </summary>
public static class RedisStreamsMediatorBuilderExtensions
{
    /// <summary>
    /// Configures the outbox to publish messages via Redis Streams.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configure">An optional action to configure <see cref="RedisStreamsTransportOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <see cref="StackExchange.Redis.IConnectionMultiplexer"/> must be registered in the DI container
    /// by the caller before the application starts.
    /// <para><strong>Note:</strong></para>
    /// Replaces any previously registered <see cref="IMessageTransport"/>.
    /// Options are also bound from the <c>Pulse:Transports:RedisStreams</c> configuration section automatically.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register IConnectionMultiplexer first
    /// services.AddSingleton&lt;IConnectionMultiplexer&gt;(_ =>
    ///     ConnectionMultiplexer.Connect("localhost:6379"));
    ///
    /// services.AddPulse(config => config
    ///     .UseRedisStreamsTransport()
    /// );
    ///
    /// // Or with custom options
    /// services.AddPulse(config => config
    ///     .UseRedisStreamsTransport(opts =>
    ///     {
    ///         opts.StreamKey = "my-app:outbox";
    ///         opts.ConsumerGroupName = "my-app-processor";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder UseRedisStreamsTransport(
        this IMediatorBuilder configurator,
        Action<RedisStreamsTransportOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        _ = services.AddOptions<RedisStreamsTransportOptions>().ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IConfigureOptions<RedisStreamsTransportOptions>,
                RedisStreamsTransportOptionsConfiguration
            >()
        );

        if (configure is not null)
        {
            _ = services.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<RedisStreamsTransportOptions>,
                RedisStreamsTransportOptionsValidator
            >()
        );

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton<IMessageTransport, RedisStreamsMessageTransport>();

        return configurator;
    }
}
