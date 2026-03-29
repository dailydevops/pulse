namespace NetEvolve.Pulse.Internals;

internal interface IServiceBusSenderAdapter : IAsyncDisposable
{
    Task<IServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken);

    Task SendMessageAsync(Azure.Messaging.ServiceBus.ServiceBusMessage message, CancellationToken cancellationToken);

    Task SendMessagesAsync(IServiceBusMessageBatch batch, CancellationToken cancellationToken);
}
