namespace NetEvolve.Pulse.Tests.Unit.Kafka;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Phase 2 audit verification — Q06.
/// <see cref="KafkaMessageTransport.SendBatchAsync"/> calls
/// <c>_producer.Flush(Timeout.InfiniteTimeSpan)</c> and does NOT pass the supplied
/// <see cref="CancellationToken"/>. The Confluent.Kafka <see cref="IProducer{TKey,TValue}"/>
/// interface offers a <c>Flush(CancellationToken)</c> overload that respects cancellation;
/// the transport uses the timespan overload instead, so a stuck broker blocks shutdown forever.
///
/// EXPECTED TO FAIL today: the test plugs a fake producer whose timespan-Flush blocks until
/// signalled (mirroring the real librdkafka behavior on an unreachable broker), then triggers
/// cancellation after 100 ms. <see cref="KafkaMessageTransport.SendBatchAsync"/> must complete
/// within 1 second — today it does not, so the wait-with-timeout returns false and the assertion fails.
/// </summary>
[TestGroup("Audit-Q06")]
public sealed class KafkaMessageTransportCancellationTests
{
    [Test]
    public async Task SendBatchAsync_Respects_CancellationToken_When_Flush_Hangs()
    {
        using var producer = new BlockingFakeProducer();
        using var admin = new BlockingFakeAdminClient();
        var transport = new KafkaMessageTransport(producer, admin, new FixedTopicNameResolver("test-topic"));
        var message = CreateOutboxMessage();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var sendTask = Task.Run(() => transport.SendBatchAsync(new[] { message }, cts.Token), CancellationToken.None);

        // ASSERTION CAPTURES THE DEFECT:
        // SendBatchAsync should respect the cancellation token. Today it ignores it because
        // the transport calls _producer.Flush(Timeout.InfiniteTimeSpan) instead of
        // _producer.Flush(cancellationToken).
        // We give a generous 1-second window for the cancellation to propagate; today the call
        // hangs until producer.Release() is called explicitly, so the wait times out.
        var completedInTime =
            await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false) == sendTask;

        // Release the producer so we don't leak the background task.
        producer.Release();
        try
        {
            await sendTask.ConfigureAwait(false);
        }
        catch
        {
            // Ignore: the task may surface AggregateException or OperationCanceledException.
        }

        _ = await Assert.That(completedInTime).IsTrue();
    }

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestKafkaEvent),
            Payload = """{"event":"sample"}""",
            CorrelationId = "corr-q06",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 0,
        };

    private sealed record TestKafkaEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class FixedTopicNameResolver(string topic) : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => topic;
    }

    /// <summary>
    /// A fake producer whose <c>Flush(TimeSpan)</c> blocks indefinitely on a manual signal,
    /// regardless of the supplied timeout. Mirrors librdkafka's real behavior on a stuck broker.
    /// The <c>Flush(CancellationToken)</c> overload honors the token (this is the fix path —
    /// the transport should call this overload).
    /// </summary>
    private sealed class BlockingFakeProducer : IProducer<string, string>
    {
        private readonly ManualResetEventSlim _release = new(initialState: false);

        public string Name => "blocking-fake-producer";
        public Handle Handle => default!;

        public void Release() => _release.Set();

        public Task<DeliveryResult<string, string>> ProduceAsync(
            string topic,
            Message<string, string> message,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new DeliveryResult<string, string>
                {
                    Topic = topic,
                    Message = message,
                    Status = PersistenceStatus.Persisted,
                }
            );

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
            // No-op: pretend the message was enqueued. Flush is the bottleneck.
        }

        public void Produce(
            TopicPartition topicPartition,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null
        ) => throw new NotSupportedException();

        public int Flush(TimeSpan timeout)
        {
            // Block until explicitly released. This is the buggy code path — the production
            // transport calls this overload with Timeout.InfiniteTimeSpan and ignores the
            // SendBatchAsync cancellation token.
            _release.Wait();
            return 0;
        }

        public void Flush(CancellationToken cancellationToken = default) =>
            // The cancellation-aware overload — the fix path.
            _release.Wait(cancellationToken);

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

        public void Dispose() => _release.Dispose();
    }

    private sealed class BlockingFakeAdminClient : IAdminClient
    {
        public string Name => "blocking-fake-admin";
        public Handle Handle => default!;

        public Metadata GetMetadata(TimeSpan timeout) => new([], [], -1, "test-cluster");

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
}
