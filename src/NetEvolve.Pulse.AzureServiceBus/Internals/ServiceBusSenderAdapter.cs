namespace NetEvolve.Pulse.Internals;

using Azure.Messaging.ServiceBus;

internal sealed class ServiceBusSenderAdapter : IServiceBusSenderAdapter
{
    private readonly ServiceBusSender _sender;

    public ServiceBusSenderAdapter(ServiceBusSender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;
    }

    public async Task<IServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken)
    {
        var batch = await _sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);
        return new ServiceBusMessageBatchAdapter(batch);
    }

    public Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken) =>
        _sender.SendMessageAsync(message, cancellationToken);

    public Task SendMessagesAsync(IServiceBusMessageBatch batch, CancellationToken cancellationToken) =>
        _sender.SendMessagesAsync(batch.InnerBatch, cancellationToken);

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
