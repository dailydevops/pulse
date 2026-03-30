namespace NetEvolve.Pulse.Internals;

using Confluent.Kafka;

internal sealed class KafkaProducerAdapter : IKafkaProducerAdapter
{
    private readonly Func<
        string,
        Message<string, string>,
        CancellationToken,
        Task<DeliveryResult<string, string>>
    > _produceAsync;
    private readonly Action<string, Message<string, string>, Action<DeliveryReport<string, string>>?> _produce;
    private readonly Func<TimeSpan, int> _flush;
    private readonly Action _dispose;

    public KafkaProducerAdapter(IProducer<string, string> producer)
    {
        ArgumentNullException.ThrowIfNull(producer);

        _produceAsync = (topic, message, ct) => producer.ProduceAsync(topic, message, ct);
        _produce = (topic, message, handler) => producer.Produce(topic, message, handler);
        _flush = producer.Flush;
        _dispose = producer.Dispose;
    }

    internal KafkaProducerAdapter(
        Func<string, Message<string, string>, CancellationToken, Task<DeliveryResult<string, string>>> produceAsync,
        Action<string, Message<string, string>, Action<DeliveryReport<string, string>>?> produce,
        Func<TimeSpan, int> flush,
        Action dispose
    )
    {
        ArgumentNullException.ThrowIfNull(produceAsync);
        ArgumentNullException.ThrowIfNull(produce);
        ArgumentNullException.ThrowIfNull(flush);
        ArgumentNullException.ThrowIfNull(dispose);

        _produceAsync = produceAsync;
        _produce = produce;
        _flush = flush;
        _dispose = dispose;
    }

    /// <inheritdoc />
    public Task<DeliveryResult<string, string>> ProduceAsync(
        string topic,
        Message<string, string> message,
        CancellationToken cancellationToken
    ) => _produceAsync(topic, message, cancellationToken);

    /// <inheritdoc />
    public void Produce(
        string topic,
        Message<string, string> message,
        Action<DeliveryReport<string, string>>? deliveryHandler = null
    ) => _produce(topic, message, deliveryHandler);

    /// <inheritdoc />
    public int Flush(TimeSpan timeout) => _flush(timeout);

    /// <inheritdoc />
    public void Dispose() => _dispose();
}
