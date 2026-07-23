namespace NetEvolve.Pulse.Tests.Unit.Kafka;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Kafka")]
public sealed class KafkaMessageTransportTests
{
    [Test]
    public async Task SendAsync_Maps_outbox_message_to_kafka_message(CancellationToken cancellationToken)
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        var kafkaMessage = producer.ProducedMessages.Single();
        using (Assert.Multiple())
        {
            _ = await Assert.That(kafkaMessage.Key).IsEqualTo(outboxMessage.Id.ToString("D"));
            _ = await Assert.That(kafkaMessage.Value).IsEqualTo(outboxMessage.Payload);
            _ = await Assert
                .That(GetHeader(kafkaMessage, "eventType"))
                .IsEqualTo(outboxMessage.EventType.ToOutboxEventTypeName());
            _ = await Assert.That(GetHeader(kafkaMessage, "contentType")).IsEqualTo("application/json");
            _ = await Assert.That(GetHeader(kafkaMessage, "correlationId")).IsEqualTo(outboxMessage.CorrelationId);
        }
    }

    [Test]
    public async Task SendAsync_Propagates_ProduceException_on_delivery_failure(CancellationToken cancellationToken)
    {
        var expectedError = new Error(ErrorCode.BrokerNotAvailable, "broker unavailable");
        using var producer = new FakeProducer { ProduceAsyncError = expectedError };
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var outboxMessage = CreateOutboxMessage();

        var exception = await Assert.ThrowsAsync<ProduceException<string, string>>(() =>
            transport.SendAsync(outboxMessage, cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.Error.Code).IsEqualTo(ErrorCode.BrokerNotAvailable);
    }

    [Test]
    public async Task SendAsync_Uses_topic_name_resolver_to_determine_topic(CancellationToken cancellationToken)
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin, topicName: "resolved-topic");
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(producer.ProducedTopics.Single()).IsEqualTo("resolved-topic");
    }

    [Test]
    public async Task SendAsync_Routes_to_topic_from_resolver(CancellationToken cancellationToken)
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin, topicName: "test-topic");
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(producer.ProducedTopics.Single()).IsEqualTo("test-topic");
    }

    [Test]
    public async Task SendBatchAsync_Enqueues_all_messages_and_flushes(CancellationToken cancellationToken)
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var messages = Enumerable.Range(0, 3).Select(_ => CreateOutboxMessage()).ToArray();

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(producer.EnqueuedMessages.Count).IsEqualTo(messages.Length);
        _ = await Assert.That(producer.FlushCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SendBatchAsync_Collects_delivery_errors_as_AggregateException(CancellationToken cancellationToken)
    {
        var error = new Error(ErrorCode.BrokerNotAvailable, "broker down");
        using var producer = new FakeProducer { DeliveryError = error };
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage() };

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            transport.SendBatchAsync(messages, cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.InnerExceptions.Count).IsEqualTo(messages.Length);
    }

    // INVARIANT: Even when delivery reports indicate errors, the producer's Flush()
    // MUST be invoked (otherwise enqueued, in-flight messages could be lost on disposal
    // and Acks.All durability is undermined).
    [Test]
    public async Task SendBatchAsync_Flushes_producer_even_when_delivery_errors_occur(
        CancellationToken cancellationToken
    )
    {
        var error = new Error(ErrorCode.BrokerNotAvailable, "down");
        using var producer = new FakeProducer { DeliveryError = error };
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage() };

        _ = await Assert.ThrowsAsync<AggregateException>(() => transport.SendBatchAsync(messages, cancellationToken));

        // The aggregate must wrap the per-message delivery errors after Flush completes.
        _ = await Assert.That(producer.FlushCallCount).IsEqualTo(1);
        _ = await Assert.That(producer.EnqueuedMessages.Count).IsEqualTo(messages.Length);
    }

    // INVARIANT: Synchronous Produce() failures (e.g., local queue full) must also be
    // collected and surfaced via AggregateException after Flush completes.
    [Test]
    public async Task SendBatchAsync_Collects_synchronous_produce_exceptions(CancellationToken cancellationToken)
    {
        var error = new Error(ErrorCode.Local_QueueFull, "queue full");
        using var producer = new FakeProducer { ThrowOnProduce = error };
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage() };

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            transport.SendBatchAsync(messages, cancellationToken)
        );

        _ = await Assert.That(ex!.InnerExceptions.Count).IsEqualTo(messages.Length);
        _ = await Assert.That(producer.FlushCallCount).IsEqualTo(1);
    }

    // INVARIANT: A successful batch must NOT throw and Flush() must be called exactly once.
    [Test]
    public async Task SendBatchAsync_When_all_messages_succeed_calls_Flush_once(CancellationToken cancellationToken)
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var messages = Enumerable.Range(0, 5).Select(_ => CreateOutboxMessage()).ToArray();

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(producer.FlushCallCount).IsEqualTo(1);
        _ = await Assert.That(producer.EnqueuedMessages.Count).IsEqualTo(messages.Length);
    }

    // INVARIANT: SendBatchAsync forwards the caller's CancellationToken to IProducer.Flush so a
    // shutdown (cancellation) while flushing surfaces OperationCanceledException rather than
    // blocking indefinitely against an unreachable broker.
    [Test]
    public async Task SendBatchAsync_Forwards_cancellation_token_to_Flush(CancellationToken cancellationToken)
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var messages = new[] { CreateOutboxMessage() };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await transport.SendBatchAsync(messages, cts.Token).ConfigureAwait(false);

        _ = await Assert.That(producer.FlushCallCount).IsEqualTo(1);
        _ = await Assert.That(producer.FlushCancellationTokens).HasSingleItem();
        // The exact same CT instance the caller passed must reach Flush; otherwise the worker
        // shutdown cannot interrupt an in-flight flush against an unreachable broker.
        _ = await Assert.That(producer.FlushCancellationTokens[0]).IsEqualTo(cts.Token);
    }

    [Test]
    public async Task SendBatchAsync_When_cancellation_requested_during_flush_propagates_OperationCanceledException(
        CancellationToken cancellationToken
    )
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage() };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cts.CancelAsync().ConfigureAwait(false);

        // The CT is already cancelled. SendBatchAsync still iterates Produce() (which is the
        // librdkafka enqueue path and intentionally ignores CT), but Flush(ct) must observe the
        // cancellation and throw OperationCanceledException so the worker can shut down
        // promptly instead of hanging on Timeout.InfiniteTimeSpan.
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => transport.SendBatchAsync(messages, cts.Token));

        _ = await Assert.That(producer.FlushCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task IsHealthyAsync_Returns_true_when_broker_metadata_is_available(CancellationToken cancellationToken)
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsTrue();
        _ = await Assert.That(admin.GetMetadataCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task IsHealthyAsync_Returns_false_when_no_brokers_in_metadata(CancellationToken cancellationToken)
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 0 };
        var transport = CreateTransport(producer, admin);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_Returns_false_without_throwing_when_broker_is_unreachable(
        CancellationToken cancellationToken
    )
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { ThrowOnGetMetadata = true };
        var transport = CreateTransport(producer, admin);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    private static KafkaMessageTransport CreateTransport(
        IProducer<string, string> producer,
        IAdminClient admin,
        string topicName = "test-topic"
    ) => new(producer, admin, new FixedTopicNameResolver(topicName));

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestKafkaEvent),
            Payload = """{"event":"sample"}""",
            CorrelationId = "corr-123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 1,
        };

    private static string GetHeader(Message<string, string> message, string key)
    {
        var header = message.Headers.FirstOrDefault(h => string.Equals(h.Key, key, StringComparison.Ordinal));
        return header is null ? string.Empty : Encoding.UTF8.GetString(header.GetValueBytes());
    }

    private sealed class FixedTopicNameResolver(string topic) : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => topic;
    }

    private sealed class FakeProducer : IProducer<string, string>
    {
        public List<string> ProducedTopics { get; } = [];
        public List<Message<string, string>> ProducedMessages { get; } = [];
        public List<Message<string, string>> EnqueuedMessages { get; } = [];
        public int FlushCallCount { get; private set; }
        public Error? ProduceAsyncError { get; init; }
        public Error? DeliveryError { get; init; }
        public Error? ThrowOnProduce { get; init; }

        public string Name => "fake-producer";
        public Handle Handle => default!;

        public Task<DeliveryResult<string, string>> ProduceAsync(
            string topic,
            Message<string, string> message,
            CancellationToken cancellationToken = default
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

        public Task<DeliveryResult<string, string>> ProduceAsync(
            TopicPartition topicPartition,
            Message<string, string> message,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public void Produce(
            string topic,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null
        )
        {
            if (ThrowOnProduce is not null)
            {
                throw new ProduceException<string, string>(
                    ThrowOnProduce,
                    new DeliveryResult<string, string>
                    {
                        Topic = topic,
                        Message = message,
                        Status = PersistenceStatus.NotPersisted,
                    }
                );
            }

            EnqueuedMessages.Add(message);

            if (DeliveryError is not null && deliveryHandler is not null)
            {
                deliveryHandler(
                    new DeliveryReport<string, string>
                    {
                        Topic = topic,
                        Message = message,
                        Status = PersistenceStatus.NotPersisted,
                        Error = DeliveryError,
                    }
                );
            }
        }

        public void Produce(
            TopicPartition topicPartition,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null
        ) => throw new NotSupportedException();

        public int Flush(TimeSpan timeout)
        {
            FlushCallCount++;
            return 0;
        }

        public void Flush(CancellationToken cancellationToken = default)
        {
            FlushCallCount++;
            FlushCancellationTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public List<CancellationToken> FlushCancellationTokens { get; } = [];

        public int Poll(TimeSpan timeout) => 0;

        public void InitTransactions(TimeSpan timeout) { }

        public void BeginTransaction() { }

        public void CommitTransaction(TimeSpan timeout) { }

        public void CommitTransaction() { }

        public void AbortTransaction(TimeSpan timeout) { }

        public void AbortTransaction() { }

        public void SendOffsetsToTransaction(
            IEnumerable<TopicPartitionOffset> offsets,
            IConsumerGroupMetadata groupMetadata,
            TimeSpan timeout
        ) => throw new NotSupportedException();

        public int AddBrokers(string brokers) => 0;

        public void SetSaslCredentials(string username, string password) { }

        public void Dispose() { }
    }

    private sealed class FakeAdminClient : IAdminClient
    {
        public int BrokerCount { get; init; }
        public bool ThrowOnGetMetadata { get; init; }
        public int GetMetadataCallCount { get; private set; }

        public string Name => "fake-admin";
        public Handle Handle => default!;

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

        public Metadata GetMetadata(string topic, TimeSpan timeout) => throw new NotSupportedException();

        public List<GroupInfo> ListGroups(TimeSpan timeout) => throw new NotSupportedException();

        public GroupInfo ListGroup(string group, TimeSpan timeout) => throw new NotSupportedException();

        public Task CreateTopicsAsync(IEnumerable<TopicSpecification> topics, CreateTopicsOptions? options = null) =>
            throw new NotSupportedException();

        public Task DeleteTopicsAsync(IEnumerable<string> topics, DeleteTopicsOptions? options = null) =>
            throw new NotSupportedException();

        public Task CreatePartitionsAsync(
            IEnumerable<PartitionsSpecification> partitionsSpecifications,
            CreatePartitionsOptions? options = null
        ) => throw new NotSupportedException();

        public Task DeleteGroupsAsync(IList<string> groups, DeleteGroupsOptions? options = null) =>
            throw new NotSupportedException();

        public Task AlterConfigsAsync(
            Dictionary<ConfigResource, List<ConfigEntry>> configs,
            AlterConfigsOptions? options = null
        ) => throw new NotSupportedException();

        public Task<List<IncrementalAlterConfigsResult>> IncrementalAlterConfigsAsync(
            Dictionary<ConfigResource, List<ConfigEntry>> configs,
            IncrementalAlterConfigsOptions? options = null
        ) => throw new NotSupportedException();

        public Task<List<DescribeConfigsResult>> DescribeConfigsAsync(
            IEnumerable<ConfigResource> resources,
            DescribeConfigsOptions? options = null
        ) => throw new NotSupportedException();

        public Task<List<DeleteRecordsResult>> DeleteRecordsAsync(
            IEnumerable<TopicPartitionOffset> topicPartitionOffsets,
            DeleteRecordsOptions? options = null
        ) => throw new NotSupportedException();

        public Task CreateAclsAsync(IEnumerable<AclBinding> aclBindings, CreateAclsOptions? options = null) =>
            throw new NotSupportedException();

        public Task<DescribeAclsResult> DescribeAclsAsync(
            AclBindingFilter aclBindingFilter,
            DescribeAclsOptions? options = null
        ) => throw new NotSupportedException();

        public Task<List<DeleteAclsResult>> DeleteAclsAsync(
            IEnumerable<AclBindingFilter> aclBindingFilters,
            DeleteAclsOptions? options = null
        ) => throw new NotSupportedException();

        public Task<DeleteConsumerGroupOffsetsResult> DeleteConsumerGroupOffsetsAsync(
            string group,
            IEnumerable<TopicPartition> partitions,
            DeleteConsumerGroupOffsetsOptions? options = null
        ) => throw new NotSupportedException();

        public Task<List<AlterConsumerGroupOffsetsResult>> AlterConsumerGroupOffsetsAsync(
            IEnumerable<ConsumerGroupTopicPartitionOffsets> groupPartitions,
            AlterConsumerGroupOffsetsOptions? options = null
        ) => throw new NotSupportedException();

        public Task<List<ListConsumerGroupOffsetsResult>> ListConsumerGroupOffsetsAsync(
            IEnumerable<ConsumerGroupTopicPartitions> groupPartitions,
            ListConsumerGroupOffsetsOptions? options = null
        ) => throw new NotSupportedException();

        public Task<ListConsumerGroupsResult> ListConsumerGroupsAsync(ListConsumerGroupsOptions? options = null) =>
            throw new NotSupportedException();

        public Task<DescribeConsumerGroupsResult> DescribeConsumerGroupsAsync(
            IEnumerable<string> groups,
            DescribeConsumerGroupsOptions? options = null
        ) => throw new NotSupportedException();

        public Task<DescribeUserScramCredentialsResult> DescribeUserScramCredentialsAsync(
            IEnumerable<string> users,
            DescribeUserScramCredentialsOptions? options = null
        ) => throw new NotSupportedException();

        public Task AlterUserScramCredentialsAsync(
            IEnumerable<UserScramCredentialAlteration> alterations,
            AlterUserScramCredentialsOptions? options = null
        ) => throw new NotSupportedException();

        public int AddBrokers(string brokers) => 0;

        public void SetSaslCredentials(string username, string password) { }

        public void Dispose() { }
    }

    private sealed record TestKafkaEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
