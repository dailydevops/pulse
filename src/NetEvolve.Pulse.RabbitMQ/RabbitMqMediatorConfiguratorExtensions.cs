namespace NetEvolve.Pulse;

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
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
    /// <c>IConnection</c> must be registered in the DI container before calling this method.
    /// The RabbitMQ exchange specified in <see cref="RabbitMqTransportOptions.ExchangeName"/> must already exist.
    /// This transport does not auto-declare exchanges or queues.
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

        // Register the connection adapter
        _ = services.AddSingleton<IRabbitMqConnectionAdapter>(sp =>
        {
            var connection = sp.GetRequiredService<IConnection>();
            return new RabbitMqConnectionAdapter(connection);
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
