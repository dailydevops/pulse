namespace NetEvolve.Pulse;

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for registering the Azure Queue Storage transport with the Pulse mediator.
/// </summary>
public static class AzureQueueStorageExtensions
{
    /// <summary>
    /// Configures the outbox to deliver messages to Azure Queue Storage using a connection string.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="connectionString">The Azure Storage connection string.</param>
    /// <param name="configureOptions">Optional action to further configure <see cref="AzureQueueStorageTransportOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is <see langword="null"/> or whitespace.</exception>
    public static IMediatorBuilder UseAzureQueueStorageTransport(
        this IMediatorBuilder configurator,
        string connectionString,
        Action<AzureQueueStorageTransportOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return configurator.UseAzureQueueStorageTransportCore(
            options => options.ConnectionString = connectionString,
            configureOptions
        );
    }

    /// <summary>
    /// Configures the outbox to deliver messages to Azure Queue Storage using a service URI and managed identity.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="queueServiceUri">The Azure Queue Storage service URI (e.g., <c>https://account.queue.core.windows.net</c>).</param>
    /// <param name="configureOptions">Optional action to further configure <see cref="AzureQueueStorageTransportOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="queueServiceUri"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder UseAzureQueueStorageTransport(
        this IMediatorBuilder configurator,
        Uri queueServiceUri,
        Action<AzureQueueStorageTransportOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(queueServiceUri);

        return configurator.UseAzureQueueStorageTransportCore(
            options => options.QueueServiceUri = queueServiceUri,
            configureOptions
        );
    }

    private static IMediatorBuilder UseAzureQueueStorageTransportCore(
        this IMediatorBuilder configurator,
        Action<AzureQueueStorageTransportOptions> coreOptions,
        Action<AzureQueueStorageTransportOptions>? configureOptions
    )
    {
        var services = configurator.Services;

        _ = services.AddOptions<AzureQueueStorageTransportOptions>().Configure(coreOptions);

        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<AzureQueueStorageTransportOptions>,
                AzureQueueStorageTransportOptionsValidator
            >()
        );

        _ = services.AddOptions<AzureQueueStorageTransportOptions>().ValidateOnStart();

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton<IMessageTransport, AzureQueueStorageMessageTransport>();

        return configurator;
    }
}
