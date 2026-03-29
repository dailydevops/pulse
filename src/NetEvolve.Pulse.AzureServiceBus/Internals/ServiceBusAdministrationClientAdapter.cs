namespace NetEvolve.Pulse.Internals;

using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

internal sealed class ServiceBusAdministrationClientAdapter : IServiceBusAdministrationClientAdapter
{
    private readonly Func<string, CancellationToken, Task> _getQueueRuntimePropertiesAsync;
    private readonly Func<string, CancellationToken, Task> _getTopicRuntimePropertiesAsync;

    public ServiceBusAdministrationClientAdapter(ServiceBusAdministrationClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _getQueueRuntimePropertiesAsync = (entityPath, cancellationToken) =>
            client.GetQueueRuntimePropertiesAsync(entityPath, cancellationToken);
        _getTopicRuntimePropertiesAsync = (entityPath, cancellationToken) =>
            client.GetTopicRuntimePropertiesAsync(entityPath, cancellationToken);
    }

    internal ServiceBusAdministrationClientAdapter(
        Func<string, CancellationToken, Task> getQueueRuntimePropertiesAsync,
        Func<string, CancellationToken, Task> getTopicRuntimePropertiesAsync
    )
    {
        ArgumentNullException.ThrowIfNull(getQueueRuntimePropertiesAsync);
        ArgumentNullException.ThrowIfNull(getTopicRuntimePropertiesAsync);

        _getQueueRuntimePropertiesAsync = getQueueRuntimePropertiesAsync;
        _getTopicRuntimePropertiesAsync = getTopicRuntimePropertiesAsync;
    }

    public async Task<bool> TryGetQueueRuntimePropertiesAsync(string entityPath, CancellationToken cancellationToken)
    {
        try
        {
            await _getQueueRuntimePropertiesAsync(entityPath, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException)
        {
            return false;
        }
        catch (ServiceBusException)
        {
            return false;
        }
    }

    public async Task<bool> TryGetTopicRuntimePropertiesAsync(string entityPath, CancellationToken cancellationToken)
    {
        try
        {
            await _getTopicRuntimePropertiesAsync(entityPath, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException)
        {
            return false;
        }
        catch (ServiceBusException)
        {
            return false;
        }
    }
}
