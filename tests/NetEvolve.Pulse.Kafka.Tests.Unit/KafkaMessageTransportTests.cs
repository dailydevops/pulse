namespace NetEvolve.Pulse.Kafka.Tests.Unit;

using Confluent.Kafka;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class KafkaMessageTransportTests
{
    [Test]
    public async Task SendAsync_Maps_outbox_message_to_kafka_message()
    {
        using var producer = new FakeKafkaProducerAdapter();
        var admin = new FakeKafkaAdminAdapter { BrokerCount = 1 };
        using var transport = CreateTransport(producer, admin);
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage);

        var kafkaMessage = producer.ProducedMessages.Single();
        using (Assert.Multiple())
        {
            _ = await Assert.That(kafkaMessage.Key).IsEqualTo(outboxMessage.Id.ToString("D"));
            _ = await Assert.That(kafkaMessage.Value).IsEqualTo(outboxMessage.Payload);
            _ = await Assert.That(GetHeader(kafkaMessage, "eventType")).IsEqualTo(outboxMessage.EventType);
            _ = await Assert.That(GetHeader(kafkaMessage, "contentType")).IsEqualTo("application/json");
            _ = await Assert.That(GetHeader(kafkaMessage, "correlationId")).IsEqualTo(outboxMessage.CorrelationId);
        }
    }

    [Test]
    public async Task SendAsync_Propagates_ProduceException_on_delivery_failure()
    {
        var expectedError = new Error(ErrorCode.BrokerNotAvailable, "broker unavailable");
        using var producer = new FakeKafkaProducerAdapter { ProduceAsyncError = expectedError };
        var admin = new FakeKafkaAdminAdapter { BrokerCount = 1 };
        using var transport = CreateTransport(producer, admin);
        var outboxMessage = CreateOutboxMessage();

        var exception = await Assert.ThrowsAsync<ProduceException<string, string>>(() =>
            transport.SendAsync(outboxMessage)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.Error.Code).IsEqualTo(ErrorCode.BrokerNotAvailable);
    }

    [Test]
    public async Task SendAsync_Uses_topic_name_resolver_to_determine_topic()
    {
        using var producer = new FakeKafkaProducerAdapter();
        var admin = new FakeKafkaAdminAdapter { BrokerCount = 1 };
        using var transport = CreateTransport(producer, admin, topicName: "resolved-topic");
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage);

        _ = await Assert.That(producer.ProducedTopics.Single()).IsEqualTo("resolved-topic");
    }

    [Test]
    public async Task SendAsync_Routes_to_topic_from_resolver()
    {
        using var producer = new FakeKafkaProducerAdapter();
        var admin = new FakeKafkaAdminAdapter { BrokerCount = 1 };
        using var transport = CreateTransport(producer, admin, topicName: "test-topic");
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage);

        _ = await Assert.That(producer.ProducedTopics.Single()).IsEqualTo("test-topic");
    }

    [Test]
    public async Task SendBatchAsync_Enqueues_all_messages_and_flushes()
    {
        using var producer = new FakeKafkaProducerAdapter();
        var admin = new FakeKafkaAdminAdapter { BrokerCount = 1 };
        using var transport = CreateTransport(producer, admin);
        var messages = Enumerable.Range(0, 3).Select(_ => CreateOutboxMessage()).ToArray();

        await transport.SendBatchAsync(messages);

        _ = await Assert.That(producer.EnqueuedMessages.Count).IsEqualTo(messages.Length);
        _ = await Assert.That(producer.FlushCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SendBatchAsync_Collects_delivery_errors_as_AggregateException()
    {
        var error = new Error(ErrorCode.BrokerNotAvailable, "broker down");
        using var producer = new FakeKafkaProducerAdapter { DeliveryError = error };
        var admin = new FakeKafkaAdminAdapter { BrokerCount = 1 };
        using var transport = CreateTransport(producer, admin);
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage() };

        var exception = await Assert.ThrowsAsync<AggregateException>(() => transport.SendBatchAsync(messages));

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.InnerExceptions.Count).IsEqualTo(messages.Length);
    }

    [Test]
    public async Task IsHealthyAsync_Returns_true_when_broker_metadata_is_available()
    {
        using var producer = new FakeKafkaProducerAdapter();
        var admin = new FakeKafkaAdminAdapter { BrokerCount = 1 };
        using var transport = CreateTransport(producer, admin);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsTrue();
        _ = await Assert.That(admin.GetMetadataCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task IsHealthyAsync_Returns_false_when_no_brokers_in_metadata()
    {
        using var producer = new FakeKafkaProducerAdapter();
        var admin = new FakeKafkaAdminAdapter { BrokerCount = 0 };
        using var transport = CreateTransport(producer, admin);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_Returns_false_without_throwing_when_broker_is_unreachable()
    {
        using var producer = new FakeKafkaProducerAdapter();
        var admin = new FakeKafkaAdminAdapter { ThrowOnGetMetadata = true };
        using var transport = CreateTransport(producer, admin);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsFalse();
    }

    private static KafkaMessageTransport CreateTransport(
        IKafkaProducerAdapter producer,
        IKafkaAdminAdapter admin,
        string topicName = "test-topic"
    ) => new(producer, admin, new FixedTopicNameResolver(topicName));

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = "Sample.Event.Created",
            Payload = """{"event":"sample"}""",
            CorrelationId = "corr-123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 1,
        };

    private static string GetHeader(Message<string, string> message, string key)
    {
        var header = message.Headers.FirstOrDefault(h => h.Key == key);
        return header is null ? string.Empty : System.Text.Encoding.UTF8.GetString(header.GetValueBytes());
    }

    private sealed class FixedTopicNameResolver(string topic) : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => topic;
    }

    private sealed class FakeKafkaProducerAdapter : IKafkaProducerAdapter
    {
        public List<string> ProducedTopics { get; } = [];

        public List<Message<string, string>> ProducedMessages { get; } = [];

        public List<Message<string, string>> EnqueuedMessages { get; } = [];

        public int FlushCallCount { get; private set; }

        public Error? ProduceAsyncError { get; init; }

        public Error? DeliveryError { get; init; }

        public Task<DeliveryResult<string, string>> ProduceAsync(
            string topic,
            Message<string, string> message,
            CancellationToken cancellationToken
        )
        {
            if (ProduceAsyncError is not null)
            {
                throw new ProduceException<string, string>(
                    ProduceAsyncError,
                    new DeliveryResult<string, string>
                    {
                        Topic = topic,
                        Message = message,
                        Status = PersistenceStatus.NotPersisted,
                    }
                );
            }

            ProducedTopics.Add(topic);
            ProducedMessages.Add(message);
            return Task.FromResult(
                new DeliveryResult<string, string>
                {
                    Topic = topic,
                    Message = message,
                    Status = PersistenceStatus.Persisted,
                }
            );
        }

        public void Produce(
            string topic,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null
        )
        {
            EnqueuedMessages.Add(message);

            if (DeliveryError is not null && deliveryHandler is not null)
            {
                var report = new DeliveryReport<string, string>
                {
                    Topic = topic,
                    Message = message,
                    Status = PersistenceStatus.NotPersisted,
                    Error = DeliveryError,
                };
                deliveryHandler(report);
            }
        }

        public int Flush(TimeSpan timeout)
        {
            FlushCallCount++;
            return 0;
        }

        public void Dispose() { }
    }

    private sealed class FakeKafkaAdminAdapter : IKafkaAdminAdapter
    {
        public int BrokerCount { get; init; }

        public bool ThrowOnGetMetadata { get; init; }

        public int GetMetadataCallCount { get; private set; }

        public Metadata GetMetadata(TimeSpan timeout)
        {
            GetMetadataCallCount++;

            if (ThrowOnGetMetadata)
            {
                throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable));
            }

            var brokers = Enumerable
                .Range(1, BrokerCount)
                .Select(i => new BrokerMetadata(i, "localhost", 9092))
                .ToList();

            return new Metadata(brokers, [], -1, "test-cluster");
        }
    }
}
