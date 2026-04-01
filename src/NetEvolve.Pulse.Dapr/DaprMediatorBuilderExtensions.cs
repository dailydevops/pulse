namespace NetEvolve.Pulse;

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for registering the Dapr message transport with the Pulse mediator.
/// </summary>
public static class DaprMediatorBuilderExtensions
{
    /// <summary>
    /// Configures the outbox to publish messages via Dapr pub/sub.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="DaprMessageTransportOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <c>DaprClient</c> must be registered in the DI container before calling this method,
    /// for example via <c>services.AddDaprClient()</c> from the <c>Dapr.AspNetCore</c> package.
    /// <para><strong>Note:</strong></para>
    /// Replaces any previously registered <see cref="IMessageTransport"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator"/> is null.</exception>
    public static IMediatorBuilder UseDaprTransport(
        this IMediatorBuilder configurator,
        Action<DaprMessageTransportOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        _ = services.AddOptions<DaprMessageTransportOptions>();
        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton<IMessageTransport, DaprMessageTransport>();

        return configurator;
    }
}
