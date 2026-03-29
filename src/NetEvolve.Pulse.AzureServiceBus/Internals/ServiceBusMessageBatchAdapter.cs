namespace NetEvolve.Pulse.Internals;

using Azure.Messaging.ServiceBus;

internal sealed class ServiceBusMessageBatchAdapter : IServiceBusMessageBatch
{
    public ServiceBusMessageBatchAdapter(ServiceBusMessageBatch innerBatch)
    {
        ArgumentNullException.ThrowIfNull(innerBatch);
        InnerBatch = innerBatch;
    }

    public ServiceBusMessageBatch InnerBatch { get; }

    public bool TryAddMessage(ServiceBusMessage message) => InnerBatch.TryAddMessage(message);
}
