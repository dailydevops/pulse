namespace NetEvolve.Pulse.AzureServiceBus.Tests.Unit;

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class AzureServiceBusMessageTransportTests
{
    private const string FakeConnectionString =
        "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";

    // ── Constructor guards ────────────────────────────────────────────────────

    [Test]
    public async Task Constructor_When_client_is_null_throws_ArgumentNullException()
    {
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new AzureServiceBusMessageTransport(null!, resolver, options))
        );
    }

    [Test]
    public async Task Constructor_When_resolver_is_null_throws_ArgumentNullException()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var options = Options.Create(new AzureServiceBusTransportOptions());

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new AzureServiceBusMessageTransport(client, null!, options))
        );
    }

    [Test]
    public async Task Constructor_When_options_is_null_throws_ArgumentNullException()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new AzureServiceBusMessageTransport(client, resolver, null!))
        );
    }

    // ── IsHealthyAsync ────────────────────────────────────────────────────────

    [Test]
    public async Task IsHealthyAsync_When_client_not_closed_returns_true()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_When_client_is_closed_returns_false()
    {
        // Note: DisposeAsync is a no-op (does not dispose injected dependencies).
        // Close the underlying client directly to test the health check.
        var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        await client.DisposeAsync();

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsFalse();
    }

    // ── SendAsync null guard ──────────────────────────────────────────────────

    [Test]
    public async Task SendAsync_When_message_is_null_throws_ArgumentNullException()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(null!));
    }

    // ── SendAsync happy path ──────────────────────────────────────────────────

    [Test]
    public async Task SendAsync_Routes_message_to_resolver_topic()
    {
        await using var fakeClient = new FakeServiceBusClient();
        var resolver = new FakeTopicNameResolver("orders");
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(fakeClient, resolver, options);

        var message = CreateOutboxMessage();
        await transport.SendAsync(message);

        _ = await Assert.That(fakeClient.GetSender("orders")).IsNotNull();
        _ = await Assert.That(fakeClient.GetSender("orders")!.SentMessages).HasSingleItem();
    }

    [Test]
    public async Task SendAsync_Maps_required_fields_onto_ServiceBusMessage()
    {
        await using var fakeClient = new FakeServiceBusClient();
        var resolver = new FakeTopicNameResolver("my-topic");
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(fakeClient, resolver, options);

        var outboxMessage = CreateOutboxMessage();
        await transport.SendAsync(outboxMessage);

        var sent = fakeClient.GetSender("my-topic")!.SentMessages[0];
        using (Assert.Multiple())
        {
            _ = await Assert.That(sent.ContentType).IsEqualTo("application/json");
            _ = await Assert.That(sent.Subject).IsEqualTo(outboxMessage.EventType);
            _ = await Assert
                .That(sent.MessageId)
                .IsEqualTo(outboxMessage.Id.ToString("D", CultureInfo.InvariantCulture));
            _ = await Assert.That(sent.CorrelationId).IsEqualTo(outboxMessage.CorrelationId);
            _ = await Assert.That(sent.Body.ToString()).IsEqualTo(outboxMessage.Payload);
            _ = await Assert.That(sent.ApplicationProperties["eventType"]).IsEqualTo(outboxMessage.EventType);
            _ = await Assert.That(sent.ApplicationProperties["retryCount"]).IsEqualTo(outboxMessage.RetryCount);
        }
    }

    [Test]
    public async Task SendAsync_Maps_optional_ProcessedAt_when_set()
    {
        await using var fakeClient = new FakeServiceBusClient();
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(fakeClient, resolver, options);

        var processedAt = DateTimeOffset.UtcNow;
        var outboxMessage = CreateOutboxMessage();
        outboxMessage.ProcessedAt = processedAt;

        await transport.SendAsync(outboxMessage);

        var sent = fakeClient.GetSender("test-topic")!.SentMessages[0];
        _ = await Assert.That(sent.ApplicationProperties["processedAt"]).IsEqualTo(processedAt);
    }

    [Test]
    public async Task SendAsync_Maps_optional_Error_when_set()
    {
        await using var fakeClient = new FakeServiceBusClient();
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(fakeClient, resolver, options);

        var outboxMessage = CreateOutboxMessage();
        outboxMessage.Error = "Some processing error";

        await transport.SendAsync(outboxMessage);

        var sent = fakeClient.GetSender("test-topic")!.SentMessages[0];
        _ = await Assert.That(sent.ApplicationProperties["error"]).IsEqualTo("Some processing error");
    }

    [Test]
    public async Task SendAsync_Does_not_add_processedAt_property_when_null()
    {
        await using var fakeClient = new FakeServiceBusClient();
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(fakeClient, resolver, options);

        var outboxMessage = CreateOutboxMessage(); // ProcessedAt is null by default

        await transport.SendAsync(outboxMessage);

        var sent = fakeClient.GetSender("test-topic")!.SentMessages[0];
        _ = await Assert.That(sent.ApplicationProperties.ContainsKey("processedAt")).IsFalse();
        _ = await Assert.That(sent.ApplicationProperties.ContainsKey("error")).IsFalse();
    }

    // ── SendBatchAsync null/empty guards ──────────────────────────────────────

    [Test]
    public async Task SendBatchAsync_When_messages_is_null_throws_ArgumentNullException()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendBatchAsync(null!));
    }

    [Test]
    public async Task SendBatchAsync_When_messages_is_empty_does_not_throw()
    {
        await using var client = new ServiceBusClient(FakeConnectionString);
        var resolver = new FakeTopicNameResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions());

        await using var transport = new AzureServiceBusMessageTransport(client, resolver, options);

        await transport.SendBatchAsync([]);
    }

    // ── SendBatchAsync – batching disabled ────────────────────────────────────

    [Test]
    public async Task SendBatchAsync_BatchingDisabled_Sends_each_message_individually()
    {
        await using var fakeClient = new FakeServiceBusClient();
        var resolver = new FakeTopicNameResolver("queue1");
        var options = Options.Create(new AzureServiceBusTransportOptions { EnableBatching = false });

        await using var transport = new AzureServiceBusMessageTransport(fakeClient, resolver, options);

        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage(), CreateOutboxMessage() };
        await transport.SendBatchAsync(messages);

        var sender = fakeClient.GetSender("queue1")!;
        _ = await Assert.That(sender.SentMessages.Count).IsEqualTo(3);
        _ = await Assert.That(sender.BatchedMessages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendBatchAsync_BatchingDisabled_Groups_messages_by_topic()
    {
        await using var fakeClient = new FakeServiceBusClient();
        var resolver = new TopicPerEventTypeResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions { EnableBatching = false });

        await using var transport = new AzureServiceBusMessageTransport(fakeClient, resolver, options);

        var messages = new[]
        {
            CreateOutboxMessage("TopicA"),
            CreateOutboxMessage("TopicA"),
            CreateOutboxMessage("TopicB"),
        };

        await transport.SendBatchAsync(messages);

        _ = await Assert.That(fakeClient.GetSender("TopicA")!.SentMessages.Count).IsEqualTo(2);
        _ = await Assert.That(fakeClient.GetSender("TopicB")!.SentMessages.Count).IsEqualTo(1);
    }

    // ── SendBatchAsync – batching enabled ─────────────────────────────────────

    [Test]
    public async Task SendBatchAsync_BatchingEnabled_Sends_messages_as_batch()
    {
        await using var fakeClient = new FakeServiceBusClient();
        var resolver = new FakeTopicNameResolver("orders");
        var options = Options.Create(new AzureServiceBusTransportOptions { EnableBatching = true });

        await using var transport = new AzureServiceBusMessageTransport(fakeClient, resolver, options);

        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage() };
        await transport.SendBatchAsync(messages);

        var sender = fakeClient.GetSender("orders")!;
        _ = await Assert.That(sender.SentMessages.Count).IsEqualTo(0);
        _ = await Assert.That(sender.BatchedMessages.Count).IsEqualTo(1);
        _ = await Assert.That(sender.BatchedMessages[0].Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendBatchAsync_BatchingEnabled_Groups_messages_by_topic()
    {
        await using var fakeClient = new FakeServiceBusClient();
        var resolver = new TopicPerEventTypeResolver();
        var options = Options.Create(new AzureServiceBusTransportOptions { EnableBatching = true });

        await using var transport = new AzureServiceBusMessageTransport(fakeClient, resolver, options);

        var messages = new[]
        {
            CreateOutboxMessage("Alpha"),
            CreateOutboxMessage("Alpha"),
            CreateOutboxMessage("Beta"),
        };

        await transport.SendBatchAsync(messages);

        _ = await Assert.That(fakeClient.GetSender("Alpha")!.BatchedMessages[0].Count).IsEqualTo(2);
        _ = await Assert.That(fakeClient.GetSender("Beta")!.BatchedMessages[0].Count).IsEqualTo(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OutboxMessage CreateOutboxMessage(string eventType = "Test.Event") =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = """{"data":"test"}""",
            CorrelationId = "corr-123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 0,
        };

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        private readonly string _topicName;

        public FakeTopicNameResolver(string topicName = "test-topic") => _topicName = topicName;

        public string Resolve(OutboxMessage message) => _topicName;
    }

    private sealed class TopicPerEventTypeResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => message.EventType;
    }

    private sealed class FakeServiceBusClient : ServiceBusClient
    {
        private readonly Dictionary<string, FakeServiceBusSender> _senders = new(StringComparer.Ordinal);

        public FakeServiceBusSender? GetSender(string name) => _senders.TryGetValue(name, out var s) ? s : null;

        public override ServiceBusSender CreateSender(string queueOrTopicName)
        {
            var sender = new FakeServiceBusSender();
            _senders[queueOrTopicName] = sender;
            return sender;
        }

        // No real connection to dispose – suppress base call to avoid NullReferenceException.
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2215",
            Justification = "Test fake with no underlying transport; calling base would throw NullReferenceException."
        )]
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeServiceBusSender : ServiceBusSender
    {
        public List<ServiceBusMessage> SentMessages { get; } = [];
        public List<IReadOnlyList<ServiceBusMessage>> BatchedMessages { get; } = [];

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public override Task SendMessagesAsync(
            IEnumerable<ServiceBusMessage> messages,
            CancellationToken cancellationToken = default
        )
        {
            BatchedMessages.Add(messages.ToList());
            return Task.CompletedTask;
        }

        // No real connection to dispose – suppress base call to avoid NullReferenceException.
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2215",
            Justification = "Test fake with no underlying transport; calling base would throw NullReferenceException."
        )]
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
