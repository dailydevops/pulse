namespace NetEvolve.Pulse;

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using RabbitMQ.Client;

/// <summary>
/// Extension methods for registering the RabbitMQ message transport with the Pulse mediator.
/// </summary>
public static class RabbitMqMediatorConfiguratorExtensions
{
    /// <summary>
    /// Configures the outbox to publish messages via RabbitMQ.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="RabbitMqTransportOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// The RabbitMQ exchange specified in <see cref="RabbitMqTransportOptions.ExchangeName"/> must already exist.
    /// This transport does not auto-declare exchanges or queues.
    /// <para><strong>Connection Management:</strong></para>
    /// Registers a singleton <see cref="IConnection"/> using <see cref="RabbitMqTransportOptions"/> configuration.
    /// The connection is created lazily on first use and shared across all message sends.
    /// <para><strong>Note:</strong></para>
    /// Replaces any previously registered <see cref="IMessageTransport"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator"/> is null.</exception>
    public static IMediatorConfigurator UseRabbitMqTransport(
        this IMediatorConfigurator configurator,
        Action<RabbitMqTransportOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        _ = services.AddOptions<RabbitMqTransportOptions>();
        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        // Register RabbitMQ connection as a singleton
        _ = services.AddSingleton<IConnection>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RabbitMqTransportOptions>>().Value;
            var factory = new ConnectionFactory
            {
                HostName = options.HostName,
                Port = options.Port,
                VirtualHost = options.VirtualHost,
                UserName = options.UserName,
                Password = options.Password,
            };

#pragma warning disable VSTHRD002 // Synchronously waiting is acceptable in DI registration
            return factory.CreateConnectionAsync().ConfigureAwait(false).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        });

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton<IMessageTransport, RabbitMqMessageTransport>();

        return configurator;
    }
}
