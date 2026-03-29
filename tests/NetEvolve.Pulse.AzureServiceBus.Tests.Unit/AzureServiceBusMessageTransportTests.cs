namespace NetEvolve.Pulse.AzureServiceBus.Tests.Unit;

using System.Linq;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class AzureServiceBusMessageTransportTests
{
    [Test]
    public async Task SendAsync_Maps_outbox_message_to_service_bus_message()
    {
        await using var sender = new FakeServiceBusSenderAdapter();
        var admin = new FakeAdministrationClientAdapter { QueueHealthy = true };
        await using var transport = CreateTransport(sender, admin);
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage);

        var serviceBusMessage = sender.SingleMessages.Single();
        using (Assert.Multiple())
        {
            _ = await Assert.That(serviceBusMessage.Subject).IsEqualTo(outboxMessage.EventType);
            _ = await Assert.That(serviceBusMessage.MessageId).IsEqualTo(outboxMessage.Id.ToString());
            _ = await Assert.That(serviceBusMessage.CorrelationId).IsEqualTo(outboxMessage.CorrelationId);
            _ = await Assert.That(serviceBusMessage.ContentType).IsEqualTo("application/json");
            _ = await Assert
                .That(serviceBusMessage.ApplicationProperties["retryCount"])
                .IsEqualTo(outboxMessage.RetryCount);
            _ = await Assert
                .That(serviceBusMessage.ApplicationProperties["eventType"])
                .IsEqualTo(outboxMessage.EventType);
        }

        var bodyText = serviceBusMessage.Body.ToString();
        _ = await Assert.That(bodyText).IsEqualTo(outboxMessage.Payload);
    }

    [Test]
    public async Task SendBatchAsync_When_batching_enabled_sends_single_batch()
    {
        await using var sender = new FakeServiceBusSenderAdapter();
        var admin = new FakeAdministrationClientAdapter { QueueHealthy = true };
        await using var transport = CreateTransport(sender, admin);
        var messages = Enumerable.Range(0, 3).Select(_ => CreateOutboxMessage()).ToArray();

        await transport.SendBatchAsync(messages);

        _ = await Assert.That(sender.CreateBatchCallCount).IsEqualTo(1);
        var batch = sender.SentBatches.Single();
        _ = await Assert.That(batch.Messages.Count).IsEqualTo(messages.Length);
    }

    [Test]
    public async Task SendBatchAsync_When_message_exceeds_batch_throws()
    {
        await using var sender = new FakeServiceBusSenderAdapter(allowAdd: false);
        var admin = new FakeAdministrationClientAdapter { QueueHealthy = true };
        await using var transport = CreateTransport(sender, admin);
        var messages = new[] { CreateOutboxMessage() };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendBatchAsync(messages));

        _ = await Assert.That(exception?.Message).Contains("exceeded the maximum batch size");
    }

    [Test]
    public async Task IsHealthyAsync_When_queue_unavailable_returns_false()
    {
        await using var sender = new FakeServiceBusSenderAdapter();
        var admin = new FakeAdministrationClientAdapter { QueueHealthy = false };
        await using var transport = CreateTransport(sender, admin);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsFalse();
        _ = await Assert.That(admin.QueueCalls).IsEqualTo(1);
    }

    [Test]
    public async Task IsHealthyAsync_When_queue_healthy_returns_true()
    {
        await using var sender = new FakeServiceBusSenderAdapter();
        var admin = new FakeAdministrationClientAdapter { QueueHealthy = true };
        await using var transport = CreateTransport(sender, admin);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsTrue();
        _ = await Assert.That(admin.QueueCalls).IsEqualTo(1);
    }

    [Test]
    public async Task IsHealthyAsync_When_entity_type_is_topic_checks_topic()
    {
        await using var sender = new FakeServiceBusSenderAdapter();
        var admin = new FakeAdministrationClientAdapter { TopicHealthy = true };
        await using var transport = CreateTransport(sender, admin, entityType: AzureServiceBusEntityType.Topic);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsTrue();
        _ = await Assert.That(admin.TopicCalls).IsEqualTo(1);
        _ = await Assert.That(admin.QueueCalls).IsEqualTo(0);
    }

    private static AzureServiceBusMessageTransport CreateTransport(
        IServiceBusSenderAdapter sender,
        IServiceBusAdministrationClientAdapter admin,
        bool enableBatching = true,
        AzureServiceBusEntityType entityType = AzureServiceBusEntityType.Queue
    )
    {
        var options = Options.Create(
            new AzureServiceBusTransportOptions
            {
                ConnectionString =
                    "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=",
                EntityPath = "queue",
                EnableBatching = enableBatching,
                EntityType = entityType,
            }
        );

        return new AzureServiceBusMessageTransport(sender, admin, options);
    }

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
            ProcessedAt = DateTimeOffset.UtcNow,
        };

    private sealed class FakeServiceBusSenderAdapter : IServiceBusSenderAdapter
    {
        private readonly bool _allowAdd;

        internal FakeServiceBusSenderAdapter(bool allowAdd = true) => _allowAdd = allowAdd;

        internal List<ServiceBusMessage> SingleMessages { get; } = [];

        internal List<FakeServiceBusMessageBatch> SentBatches { get; } = [];

        internal int CreateBatchCallCount { get; private set; }

        public Task<IServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken)
        {
            CreateBatchCallCount++;
            IServiceBusMessageBatch batch = new FakeServiceBusMessageBatch(_allowAdd);
            return Task.FromResult(batch);
        }

        public Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken)
        {
            SingleMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task SendMessagesAsync(IServiceBusMessageBatch batch, CancellationToken cancellationToken)
        {
            if (batch is FakeServiceBusMessageBatch fakeBatch)
            {
                SentBatches.Add(fakeBatch);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeServiceBusMessageBatch(bool allowAdd) : IServiceBusMessageBatch
    {
        public List<ServiceBusMessage> Messages { get; } = [];

        public ServiceBusMessageBatch InnerBatch =>
            throw new NotSupportedException("Inner batch is not used by the fake sender adapter.");

        public bool TryAddMessage(ServiceBusMessage message)
        {
            if (!allowAdd)
            {
                return false;
            }

            Messages.Add(message);
            return true;
        }
    }

    private sealed class FakeAdministrationClientAdapter : IServiceBusAdministrationClientAdapter
    {
        public bool QueueHealthy { get; init; }

        public bool TopicHealthy { get; init; }

        public int QueueCalls { get; private set; }

        public int TopicCalls { get; private set; }

        public Task<bool> TryGetQueueRuntimePropertiesAsync(string entityPath, CancellationToken cancellationToken)
        {
            QueueCalls++;
            return Task.FromResult(QueueHealthy);
        }

        public Task<bool> TryGetTopicRuntimePropertiesAsync(string entityPath, CancellationToken cancellationToken)
        {
            TopicCalls++;
            return Task.FromResult(TopicHealthy);
        }
    }
}
