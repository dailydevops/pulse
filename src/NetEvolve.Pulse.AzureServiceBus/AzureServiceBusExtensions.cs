namespace NetEvolve.Pulse;

using System.Linq;
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for registering the Azure Service Bus transport with the Pulse mediator.
/// </summary>
public static class AzureServiceBusExtensions
{
    /// <summary>
    /// Configures the outbox to deliver messages to Azure Service Bus queues or topics.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="AzureServiceBusTransportOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder UseAzureServiceBusTransport(
        this IMediatorBuilder configurator,
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

        services.TryAddSingleton(static sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureServiceBusTransportOptions>>().Value;
            ValidateOptions(options);

            return CreateServiceBusClient(options, sp.GetRequiredService<TokenCredential>());
        });

        services.TryAddSingleton<TokenCredential, DefaultAzureCredential>();

        var existing = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton<IMessageTransport, AzureServiceBusMessageTransport>();

        return configurator;
    }

    private static ServiceBusClient CreateServiceBusClient(
        AzureServiceBusTransportOptions options,
        TokenCredential credential
    )
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new ServiceBusClient(options.ConnectionString);
        }

        return new ServiceBusClient(options.FullyQualifiedNamespace!, credential);
    }

    private static void ValidateOptions(AzureServiceBusTransportOptions options)
    {
        if (
            string.IsNullOrWhiteSpace(options.ConnectionString)
            && string.IsNullOrWhiteSpace(options.FullyQualifiedNamespace)
        )
        {
            throw new InvalidOperationException(
                "Either a Service Bus connection string or a fully qualified namespace must be provided."
            );
        }
    }
}
