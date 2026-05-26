namespace NetEvolve.Pulse.Tests.Unit.Kafka;

using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>
/// Phase 2 audit U10: <see cref="KafkaMessageTransport.SendBatchAsync"/> ignores its
/// <see cref="CancellationToken"/> and calls <c>_producer.Flush(Timeout.InfiniteTimeSpan)</c>.
/// When the broker is stuck, the call never returns even after the caller cancels the token.
/// See <c>audit/verification/round-01-U10.md</c>.
/// </summary>
[TestGroup("Kafka")]
public sealed class KafkaMessageTransportCancellationTests
{
    [Test]
    public async Task SendBatchAsync_should_observe_cancellation_within_a_bounded_time_when_Flush_is_stuck()
    {
        // ARRANGE — Producer whose Flush blocks indefinitely on a manual reset event.
        using var flushBarrier = new ManualResetEventSlim(initialState: false);
        using var producer = new BlockingFlushProducer(flushBarrier);
        using var admin = new InertAdminClient();

        var transport = new KafkaMessageTransport(producer, admin, new ConstantTopicNameResolver("topic"));
        var messages = Enumerable.Range(0, 2).Select(_ => CreateOutboxMessage()).ToArray();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // ACT — Start the batch; assert it returns within 200ms even though Flush is stuck.
        var sendBatch = transport.SendBatchAsync(messages, cts.Token);

        try
        {
            // WaitAsync enforces the 200ms ceiling externally — it does NOT depend on
            // the transport actually observing the token. If WaitAsync throws
            // TimeoutException, that proves the transport never returned in time.
            await sendBatch.WaitAsync(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Acceptable: cancellation propagated correctly.
        }
        catch (TimeoutException)
        {
            // Today: WaitAsync throws TimeoutException because Flush never returns.
            // Phase 3 fix should make this branch unreachable; this assertion fails today.
            _ = await Assert.That(sendBatch.IsCompleted).IsTrue();
            throw;
        }
        finally
        {
            // Unblock the fake Flush so the background task can drain (best-effort cleanup).
            flushBarrier.Set();
        }

        // ASSERT — After the await, the task must be in a terminal state.
        _ = await Assert.That(sendBatch.IsCompleted).IsTrue();
    }

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestKafkaEvent),
            Payload = """{"event":"sample"}""",
            CorrelationId = "corr-1",
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

    private sealed class ConstantTopicNameResolver(string topic) : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => topic;
    }

    private sealed class BlockingFlushProducer : IProducer<string, string>
    {
        private readonly ManualResetEventSlim _flushBarrier;

        public BlockingFlushProducer(ManualResetEventSlim flushBarrier) => _flushBarrier = flushBarrier;

        public string Name => "blocking-flush-producer";
        public Handle Handle => default!;

        public Task<DeliveryResult<string, string>> ProduceAsync(
            string topic,
            Message<string, string> message,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

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
            // Enqueue is a no-op for this test — only Flush is exercised.
        }

        public void Produce(
            TopicPartition topicPartition,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null
        ) => throw new NotSupportedException();

        public int Flush(TimeSpan timeout)
        {
            // Mirrors a stuck broker. Wait without observing any token — that is the bug.
            _flushBarrier.Wait();
            return 0;
        }

        public void Flush(CancellationToken cancellationToken = default) => _flushBarrier.Wait(cancellationToken);

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

    private sealed class InertAdminClient : IAdminClient
    {
        public string Name => "inert-admin";
        public Handle Handle => default!;

        public Metadata GetMetadata(TimeSpan timeout) => new([], [], -1, "cluster");

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
