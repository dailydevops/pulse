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
    public async Task SendAsync_Maps_outbox_message_to_kafka_message()
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage);

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
    public async Task SendAsync_Propagates_ProduceException_on_delivery_failure()
    {
        var expectedError = new Error(ErrorCode.BrokerNotAvailable, "broker unavailable");
        using var producer = new FakeProducer { ProduceAsyncError = expectedError };
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
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
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin, topicName: "resolved-topic");
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage);

        _ = await Assert.That(producer.ProducedTopics.Single()).IsEqualTo("resolved-topic");
    }

    [Test]
    public async Task SendAsync_Routes_to_topic_from_resolver()
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin, topicName: "test-topic");
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage);

        _ = await Assert.That(producer.ProducedTopics.Single()).IsEqualTo("test-topic");
    }

    [Test]
    public async Task SendBatchAsync_Enqueues_all_messages_and_flushes()
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var messages = Enumerable.Range(0, 3).Select(_ => CreateOutboxMessage()).ToArray();

        await transport.SendBatchAsync(messages);

        _ = await Assert.That(producer.EnqueuedMessages.Count).IsEqualTo(messages.Length);
        _ = await Assert.That(producer.FlushCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SendBatchAsync_Collects_delivery_errors_as_AggregateException()
    {
        var error = new Error(ErrorCode.BrokerNotAvailable, "broker down");
        using var producer = new FakeProducer { DeliveryError = error };
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage() };

        var exception = await Assert.ThrowsAsync<AggregateException>(() => transport.SendBatchAsync(messages));

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.InnerExceptions.Count).IsEqualTo(messages.Length);
    }

    [Test]
    public async Task IsHealthyAsync_Returns_true_when_broker_metadata_is_available()
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 1 };
        var transport = CreateTransport(producer, admin);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsTrue();
        _ = await Assert.That(admin.GetMetadataCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task IsHealthyAsync_Returns_false_when_no_brokers_in_metadata()
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { BrokerCount = 0 };
        var transport = CreateTransport(producer, admin);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_Returns_false_without_throwing_when_broker_is_unreachable()
    {
        using var producer = new FakeProducer();
        using var admin = new FakeAdminClient { ThrowOnGetMetadata = true };
        var transport = CreateTransport(producer, admin);

        var healthy = await transport.IsHealthyAsync();

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
        var header = message.Headers.FirstOrDefault(h => h.Key == key);
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
        ) => throw new NotImplementedException();

        public void Produce(
            string topic,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null
        )
        {
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
        ) => throw new NotImplementedException();

        public int Flush(TimeSpan timeout)
        {
            FlushCallCount++;
            return 0;
        }

        public void Flush(CancellationToken cancellationToken = default) { }

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
        ) => throw new NotImplementedException();

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

        public Metadata GetMetadata(string topic, TimeSpan timeout) => throw new NotImplementedException();

        public List<GroupInfo> ListGroups(TimeSpan timeout) => throw new NotImplementedException();

        public GroupInfo ListGroup(string group, TimeSpan timeout) => throw new NotImplementedException();

        public Task CreateTopicsAsync(IEnumerable<TopicSpecification> topics, CreateTopicsOptions? options = null) =>
            throw new NotImplementedException();

        public Task DeleteTopicsAsync(IEnumerable<string> topics, DeleteTopicsOptions? options = null) =>
            throw new NotImplementedException();

        public Task CreatePartitionsAsync(
            IEnumerable<PartitionsSpecification> partitionsSpecifications,
            CreatePartitionsOptions? options = null
        ) => throw new NotImplementedException();

        public Task DeleteGroupsAsync(IList<string> groups, DeleteGroupsOptions? options = null) =>
            throw new NotImplementedException();

        public Task AlterConfigsAsync(
            Dictionary<ConfigResource, List<ConfigEntry>> configs,
            AlterConfigsOptions? options = null
        ) => throw new NotImplementedException();

        public Task<List<IncrementalAlterConfigsResult>> IncrementalAlterConfigsAsync(
            Dictionary<ConfigResource, List<ConfigEntry>> configs,
            IncrementalAlterConfigsOptions? options = null
        ) => throw new NotImplementedException();

        public Task<List<DescribeConfigsResult>> DescribeConfigsAsync(
            IEnumerable<ConfigResource> resources,
            DescribeConfigsOptions? options = null
        ) => throw new NotImplementedException();

        public Task<List<DeleteRecordsResult>> DeleteRecordsAsync(
            IEnumerable<TopicPartitionOffset> topicPartitionOffsets,
            DeleteRecordsOptions? options = null
        ) => throw new NotImplementedException();

        public Task CreateAclsAsync(IEnumerable<AclBinding> aclBindings, CreateAclsOptions? options = null) =>
            throw new NotImplementedException();

        public Task<DescribeAclsResult> DescribeAclsAsync(
            AclBindingFilter aclBindingFilter,
            DescribeAclsOptions? options = null
        ) => throw new NotImplementedException();

        public Task<List<DeleteAclsResult>> DeleteAclsAsync(
            IEnumerable<AclBindingFilter> aclBindingFilters,
            DeleteAclsOptions? options = null
        ) => throw new NotImplementedException();

        public Task<DeleteConsumerGroupOffsetsResult> DeleteConsumerGroupOffsetsAsync(
            string group,
            IEnumerable<TopicPartition> partitions,
            DeleteConsumerGroupOffsetsOptions? options = null
        ) => throw new NotImplementedException();

        public Task<List<AlterConsumerGroupOffsetsResult>> AlterConsumerGroupOffsetsAsync(
            IEnumerable<ConsumerGroupTopicPartitionOffsets> groupPartitions,
            AlterConsumerGroupOffsetsOptions? options = null
        ) => throw new NotImplementedException();

        public Task<List<ListConsumerGroupOffsetsResult>> ListConsumerGroupOffsetsAsync(
            IEnumerable<ConsumerGroupTopicPartitions> groupPartitions,
            ListConsumerGroupOffsetsOptions? options = null
        ) => throw new NotImplementedException();

        public Task<ListConsumerGroupsResult> ListConsumerGroupsAsync(ListConsumerGroupsOptions? options = null) =>
            throw new NotImplementedException();

        public Task<DescribeConsumerGroupsResult> DescribeConsumerGroupsAsync(
            IEnumerable<string> groups,
            DescribeConsumerGroupsOptions? options = null
        ) => throw new NotImplementedException();

        public Task<DescribeUserScramCredentialsResult> DescribeUserScramCredentialsAsync(
            IEnumerable<string> users,
            DescribeUserScramCredentialsOptions? options = null
        ) => throw new NotImplementedException();

        public Task AlterUserScramCredentialsAsync(
            IEnumerable<UserScramCredentialAlteration> alterations,
            AlterUserScramCredentialsOptions? options = null
        ) => throw new NotImplementedException();

        public int AddBrokers(string brokers) => 0;

        public void SetSaslCredentials(string username, string password) { }

        public void Dispose() { }
    }

    private sealed record TestKafkaEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
