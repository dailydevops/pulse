namespace NetEvolve.Pulse.Internals;

internal sealed class ServiceBusSenderAdapter : IServiceBusSenderAdapter
{
    private readonly Azure.Messaging.ServiceBus.ServiceBusSender _sender;

    public ServiceBusSenderAdapter(Azure.Messaging.ServiceBus.ServiceBusSender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;
    }

    public async Task<IServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken)
    {
        var batch = await _sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);
        return new ServiceBusMessageBatchAdapter(batch);
    }

    public Task SendMessageAsync(
        Azure.Messaging.ServiceBus.ServiceBusMessage message,
        CancellationToken cancellationToken
    ) => _sender.SendMessageAsync(message, cancellationToken);

    public Task SendMessagesAsync(IServiceBusMessageBatch batch, CancellationToken cancellationToken) =>
        _sender.SendMessagesAsync(batch.InnerBatch, cancellationToken);

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
