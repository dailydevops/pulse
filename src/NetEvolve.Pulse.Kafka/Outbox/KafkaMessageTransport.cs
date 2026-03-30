namespace NetEvolve.Pulse.Outbox;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Confluent.Kafka;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Apache Kafka transport that delivers outbox messages to Kafka topics using the Confluent.Kafka
/// producer with <c>Acks.All</c> for durability.
/// </summary>
/// <remarks>
/// The Confluent.Kafka <see cref="IProducer{TKey,TValue}" /> and <see cref="IAdminClient" /> must be
/// registered in the DI container by the caller before using this transport.
/// Topic routing is determined by the registered <see cref="ITopicNameResolver" />.
/// </remarks>
public sealed class KafkaMessageTransport : IMessageTransport
{
    private readonly IProducer<string, string> _producer;
    private readonly IAdminClient _adminClient;
    private readonly ITopicNameResolver _topicNameResolver;

    /// <summary>
    /// Initializes a new instance of <see cref="KafkaMessageTransport" />.
    /// </summary>
    /// <param name="producer">The Kafka producer, registered in DI by the caller.</param>
    /// <param name="adminClient">The Kafka admin client, registered in DI by the caller.</param>
    /// <param name="topicNameResolver">The resolver that maps each outbox message to a Kafka topic name.</param>
    public KafkaMessageTransport(
        IProducer<string, string> producer,
        IAdminClient adminClient,
        ITopicNameResolver topicNameResolver
    )
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(adminClient);
        ArgumentNullException.ThrowIfNull(topicNameResolver);

        _producer = producer;
        _adminClient = adminClient;
        _topicNameResolver = topicNameResolver;
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var topic = _topicNameResolver.Resolve(message);
        var kafkaMessage = CreateKafkaMessage(message);

        _ = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "Intentional fire-and-forget batch pattern. Produce() enqueues messages with a delivery-report callback; awaiting ProduceAsync() per message would serialize delivery and defeat the purpose of batching."
    )]
    public Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var errors = new ConcurrentBag<Exception>();

        foreach (var message in messages)
        {
            var topic = _topicNameResolver.Resolve(message);
            var kafkaMessage = CreateKafkaMessage(message);

            try
            {
                _producer.Produce(
                    topic,
                    kafkaMessage,
                    report =>
                    {
                        if (report.Error.IsError)
                        {
                            errors.Add(new ProduceException<string, string>(report.Error, report));
                        }
                    }
                );
            }
            catch (ProduceException<string, string> ex)
            {
                errors.Add(ex);
            }
        }

        _ = _producer.Flush(Timeout.InfiniteTimeSpan);

        if (!errors.IsEmpty)
        {
            return Task.FromException(new AggregateException(errors));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var metadata = _adminClient.GetMetadata(TimeSpan.FromSeconds(5));
            return Task.FromResult(metadata.Brokers.Count > 0);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }
    }

    private static Message<string, string> CreateKafkaMessage(OutboxMessage message)
    {
        var headers = new Headers
        {
            { "eventType", Encoding.UTF8.GetBytes(message.EventType) },
            { "contentType", "application/json"u8.ToArray() },
        };

        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            headers.Add("correlationId", Encoding.UTF8.GetBytes(message.CorrelationId));
        }

        return new Message<string, string>
        {
            Key = message.Id.ToString("D", CultureInfo.InvariantCulture),
            Value = message.Payload,
            Headers = headers,
        };
    }
}
