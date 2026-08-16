namespace NetEvolve.Pulse.Tests.Integration.Kafka;

using System.Text;
using global::Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="KafkaMessageTransport"/> against a real Apache Kafka broker.
/// </summary>
[ClassDataSource<KafkaContainerFixture>(Shared = SharedType.PerTestSession)]
[TestGroup("Kafka")]
[Timeout(120_000)]
[NotInParallel]
public sealed class KafkaMessageTransportIntegrationTests(KafkaContainerFixture containerFixture)
{
    [Test]
    public async Task SendAsync_Publishes_message_to_topic(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var topic = CreateUniqueTopicName();
        using var producer = CreateProducer();
        using var admin = CreateAdminClient();
        await using var transport = CreateTransport(producer, admin, new FixedTopicNameResolver(topic));
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        using var consumer = CreateConsumer(topic);
        var received = ConsumeOneMessage(consumer);

        using (Assert.Multiple())
        {
            _ = await Assert.That(received).IsNotNull();
            _ = await Assert.That(received!.Message.Key).IsEqualTo(outboxMessage.Id.ToString("D"));
            _ = await Assert.That(received.Message.Value).IsEqualTo(outboxMessage.Payload);
            _ = await Assert
                .That(GetHeader(received.Message, "eventType"))
                .IsEqualTo(outboxMessage.EventType.ToOutboxEventTypeName());
            _ = await Assert.That(GetHeader(received.Message, "contentType")).IsEqualTo("application/json");
            _ = await Assert.That(GetHeader(received.Message, "correlationId")).IsEqualTo(outboxMessage.CorrelationId);
        }
    }

    [Test]
    public async Task SendBatchAsync_Publishes_all_messages_to_topic(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const int messageCount = 5;
        var topic = CreateUniqueTopicName();
        using var producer = CreateProducer();
        using var admin = CreateAdminClient();
        await using var transport = CreateTransport(producer, admin, new FixedTopicNameResolver(topic));
        var messages = Enumerable.Range(0, messageCount).Select(_ => CreateOutboxMessage()).ToList();

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        using var consumer = CreateConsumer(topic);
        var received = ConsumeMessages(consumer, messageCount);

        _ = await Assert.That(received.Count).IsEqualTo(messageCount);
    }

    [Test]
    public async Task SendAsync_Uses_custom_topic_name_resolver_to_route_message(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var topic = $"pulse-it-custom-{Guid.NewGuid():N}";
        using var producer = CreateProducer();
        using var admin = CreateAdminClient();
        await using var transport = CreateTransport(producer, admin, new FixedTopicNameResolver(topic));
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        using var consumer = CreateConsumer(topic);
        var received = ConsumeOneMessage(consumer);

        _ = await Assert.That(received).IsNotNull();
        _ = await Assert.That(received!.Topic).IsEqualTo(topic);
    }

    [Test]
    public async Task IsHealthyAsync_When_broker_reachable_returns_true(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var producer = CreateProducer();
        using var admin = CreateAdminClient();
        await using var transport = CreateTransport(
            producer,
            admin,
            new FixedTopicNameResolver(CreateUniqueTopicName())
        );

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task SendAsync_When_AutoCreateTopics_false_and_topic_missing_throws(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var topic = CreateUniqueTopicName();
        using var producer = CreateProducer(messageTimeoutMs: 5_000);
        using var admin = CreateAdminClient();
        await using var transport = CreateTransport(
            producer,
            admin,
            new FixedTopicNameResolver(topic),
            options: new KafkaTransportOptions { AutoCreateTopics = false }
        );

        _ = await Assert
            .That(() => transport.SendAsync(CreateOutboxMessage(), cancellationToken))
            .Throws<ProduceException<string, string>>();
    }

    [Test]
    public async Task SendAsync_AutoCreateTopics_true_Creates_topic_with_custom_partition_count(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var topic = CreateUniqueTopicName();
        using var producer = CreateProducer();
        using var admin = CreateAdminClient();
        await using var transport = CreateTransport(
            producer,
            admin,
            new FixedTopicNameResolver(topic),
            options: new KafkaTransportOptions { AutoCreateTopics = true, DefaultPartitionCount = 3 }
        );

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(15));
        var topicMetadata = metadata.Topics.Single(t => string.Equals(t.Topic, topic, StringComparison.Ordinal));

        _ = await Assert.That(topicMetadata.Partitions.Count).IsEqualTo(3);
    }

    [Test]
    public async Task UseKafkaTransport_Registers_ResolvableMessageTransport_ThatPublishes(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var topic = CreateUniqueTopicName();
        using var producer = CreateProducer();
        using var admin = CreateAdminClient();

        var services = new ServiceCollection();
        _ = services.AddSingleton(producer);
        _ = services.AddSingleton(admin);
        _ = services.AddSingleton<ITopicNameResolver>(new FixedTopicNameResolver(topic));
        _ = services.AddPulse(configurator =>
            configurator.UseKafkaTransport(options => options.AutoCreateTopics = true)
        );

        await using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();

        _ = await Assert.That(transport).IsTypeOf<KafkaMessageTransport>();

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        using var consumer = CreateConsumer(topic);
        var received = ConsumeOneMessage(consumer);

        _ = await Assert.That(received).IsNotNull();
    }

    [Test]
    public async Task UseKafkaTransport_Replaces_ExistingMessageTransport_Registration(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var producer = CreateProducer();
        using var admin = CreateAdminClient();

        var services = new ServiceCollection();
        _ = services.AddSingleton(producer);
        _ = services.AddSingleton(admin);
        _ = services.AddSingleton<ITopicNameResolver>(new FixedTopicNameResolver(CreateUniqueTopicName()));
        _ = services.AddPulse(configurator =>
            configurator.UseMessageTransport<NullMessageTransport>().UseKafkaTransport()
        );

        await using var provider = services.BuildServiceProvider();

        _ = await Assert.That(provider.GetRequiredService<IMessageTransport>()).IsTypeOf<KafkaMessageTransport>();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static KafkaMessageTransport CreateTransport(
        IProducer<string, string> producer,
        IAdminClient admin,
        ITopicNameResolver resolver,
        KafkaTransportOptions? options = null
    ) => new(producer, admin, resolver, Options.Create(options ?? new KafkaTransportOptions()));

    private IProducer<string, string> CreateProducer(int? messageTimeoutMs = null)
    {
        var config = new ProducerConfig { BootstrapServers = containerFixture.ConnectionString, Acks = Acks.All };
        if (messageTimeoutMs.HasValue)
        {
            config.MessageTimeoutMs = messageTimeoutMs.Value;
        }

        return new ProducerBuilder<string, string>(config).Build();
    }

    private IAdminClient CreateAdminClient() =>
        new AdminClientBuilder(new AdminClientConfig { BootstrapServers = containerFixture.ConnectionString }).Build();

    private IConsumer<string, string> CreateConsumer(string topic)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = containerFixture.ConnectionString,
            GroupId = $"pulse-it-consumer-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);
        return consumer;
    }

    private static ConsumeResult<string, string>? ConsumeOneMessage(IConsumer<string, string> consumer) =>
        consumer.Consume(TimeSpan.FromSeconds(15));

    private static List<ConsumeResult<string, string>> ConsumeMessages(
        IConsumer<string, string> consumer,
        int expectedCount
    )
    {
        var received = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow.Add(TimeSpan.FromSeconds(15));

        while (received.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(1));
            if (result is not null)
            {
                received.Add(result);
            }
        }

        return received;
    }

    private static string CreateUniqueTopicName() => $"pulse-it-{Guid.NewGuid():N}";

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(IntegrationTestEvent),
            Payload = """{"id":"test"}""",
            CorrelationId = Guid.NewGuid().ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 0,
            ProcessedAt = null,
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

    private sealed record IntegrationTestEvent;
}
