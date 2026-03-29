namespace NetEvolve.Pulse.Internals;

using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

internal sealed class ServiceBusAdministrationClientAdapter : IServiceBusAdministrationClientAdapter
{
    private readonly ServiceBusAdministrationClient _client;

    public ServiceBusAdministrationClientAdapter(ServiceBusAdministrationClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<bool> TryGetQueueRuntimePropertiesAsync(string entityPath, CancellationToken cancellationToken)
    {
        try
        {
            _ = await _client.GetQueueRuntimePropertiesAsync(entityPath, cancellationToken).ConfigureAwait(false);
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
            _ = await _client.GetTopicRuntimePropertiesAsync(entityPath, cancellationToken).ConfigureAwait(false);
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
