namespace NetEvolve.Pulse.Internals;

using Azure.Messaging.ServiceBus;

internal interface IServiceBusSenderAdapter : IAsyncDisposable
{
    Task<IServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken);

    Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken);

    Task SendMessagesAsync(IServiceBusMessageBatch batch, CancellationToken cancellationToken);
}
