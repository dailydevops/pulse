namespace NetEvolve.Pulse;

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Extension methods for registering the Azure Service Bus message transport with the Pulse mediator.
/// </summary>
public static class AzureServiceBusMediatorConfiguratorExtensions
{
    /// <summary>
    /// Configures the outbox to publish messages via Azure Service Bus.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="AzureServiceBusTransportOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para><strong>Authentication:</strong></para>
    /// Set <see cref="AzureServiceBusTransportOptions.ConnectionString"/> for connection-string-based
    /// authentication, or set <see cref="AzureServiceBusTransportOptions.FullyQualifiedNamespace"/>
    /// to use <c>DefaultAzureCredential</c> (Managed Identity, environment variables, etc.).
    /// <para><strong>Note:</strong></para>
    /// Replaces any previously registered <see cref="IMessageTransport"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator"/> is null.</exception>
    public static IMediatorConfigurator UseAzureServiceBusTransport(
        this IMediatorConfigurator configurator,
        Action<AzureServiceBusTransportOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        _ = services.AddOptions<AzureServiceBusTransportOptions>();
        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton<IMessageTransport, AzureServiceBusMessageTransport>();

        return configurator;
    }
}
