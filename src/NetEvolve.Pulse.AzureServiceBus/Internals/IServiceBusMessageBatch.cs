namespace NetEvolve.Pulse.Internals;

using Azure.Messaging.ServiceBus;

internal interface IServiceBusMessageBatch
{
    ServiceBusMessageBatch InnerBatch { get; }

    bool TryAddMessage(ServiceBusMessage message);
}
