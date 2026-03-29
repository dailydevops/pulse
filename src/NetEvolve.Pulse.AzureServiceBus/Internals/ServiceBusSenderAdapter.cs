namespace NetEvolve.Pulse.Internals;

using Azure.Messaging.ServiceBus;

internal sealed class ServiceBusSenderAdapter : IServiceBusSenderAdapter
{
    private readonly Func<CancellationToken, Task<IServiceBusMessageBatch>> _createBatch;
    private readonly Func<ServiceBusMessage, CancellationToken, Task> _sendMessage;
    private readonly Func<IServiceBusMessageBatch, CancellationToken, Task> _sendMessages;
    private readonly Func<ValueTask> _dispose;

    public ServiceBusSenderAdapter(ServiceBusSender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);

        _createBatch = async cancellationToken => new ServiceBusMessageBatchAdapter(
            await sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false)
        );
        _sendMessage = (message, cancellationToken) => sender.SendMessageAsync(message, cancellationToken);
        _sendMessages = (batch, cancellationToken) => sender.SendMessagesAsync(batch.InnerBatch, cancellationToken);
        _dispose = sender.DisposeAsync;
    }

    internal ServiceBusSenderAdapter(
        Func<CancellationToken, Task<IServiceBusMessageBatch>> createBatch,
        Func<ServiceBusMessage, CancellationToken, Task> sendMessage,
        Func<IServiceBusMessageBatch, CancellationToken, Task> sendMessages,
        Func<ValueTask> dispose
    )
    {
        ArgumentNullException.ThrowIfNull(createBatch);
        ArgumentNullException.ThrowIfNull(sendMessage);
        ArgumentNullException.ThrowIfNull(sendMessages);
        ArgumentNullException.ThrowIfNull(dispose);

        _createBatch = createBatch;
        _sendMessage = sendMessage;
        _sendMessages = sendMessages;
        _dispose = dispose;
    }

    public async Task<IServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken)
    {
        var batch = await _createBatch(cancellationToken).ConfigureAwait(false);
        return batch;
    }

    public Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken) =>
        _sendMessage(message, cancellationToken);

    public Task SendMessagesAsync(IServiceBusMessageBatch batch, CancellationToken cancellationToken) =>
        _sendMessages(batch, cancellationToken);

    public ValueTask DisposeAsync() => _dispose();
}
