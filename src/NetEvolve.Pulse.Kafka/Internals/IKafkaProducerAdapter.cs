namespace NetEvolve.Pulse.Internals;

using Confluent.Kafka;

/// <summary>
/// Internal abstraction over <see cref="IProducer{TKey,TValue}" /> to enable unit testing
/// without a live Kafka broker.
/// </summary>
internal interface IKafkaProducerAdapter : IDisposable
{
    /// <summary>
    /// Asynchronously produces a single message to the specified topic and waits for delivery
    /// confirmation with <c>Acks.All</c>.
    /// </summary>
    Task<DeliveryResult<string, string>> ProduceAsync(
        string topic,
        Message<string, string> message,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Enqueues a message for delivery without blocking. The optional
    /// <paramref name="deliveryHandler" /> is invoked when delivery succeeds or fails.
    /// </summary>
    void Produce(
        string topic,
        Message<string, string> message,
        Action<DeliveryReport<string, string>>? deliveryHandler = null
    );

    /// <summary>
    /// Blocks until all outstanding produce requests are flushed, or the
    /// <paramref name="timeout" /> elapses. Returns the number of messages not yet flushed.
    /// </summary>
    int Flush(TimeSpan timeout);
}
