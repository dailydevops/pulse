namespace NetEvolve.Pulse.Kafka.Tests.Integration;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using Testcontainers.Kafka;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class KafkaTransportIntegrationTests : IAsyncDisposable
{
    private const string TopicName = "pulse-outbox-tests";

    private KafkaContainer? _kafkaContainer;
    private string? _bootstrapServers;

    [Before(Test)]
    public async Task StartContainerAsync()
    {
        _bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS");
        if (!string.IsNullOrWhiteSpace(_bootstrapServers))
        {
            return;
        }

        _kafkaContainer = new KafkaBuilder().WithCleanUp(true).Build();
        await _kafkaContainer.StartAsync();
        _bootstrapServers = _kafkaContainer.GetBootstrapAddress();
    }

    [Test]
    public async Task SendAsync_Delivers_message_to_kafka_topic()
    {
        if (string.IsNullOrWhiteSpace(_bootstrapServers))
        {
            return;
        }

        await EnsureTopicExistsAsync(_bootstrapServers, TopicName);

        using var producer = BuildProducerAdapter(_bootstrapServers);
        using var admin = BuildAdminAdapter(_bootstrapServers);
        using var transport = new KafkaMessageTransport(producer, admin, new FixedTopicNameResolver(TopicName));

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "Integration.Event",
            Payload = """{"event":"integration"}""",
            CorrelationId = "corr-integration",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await transport.SendAsync(message);

        var received = await ConsumeOneMessageAsync(_bootstrapServers, TopicName);
        _ = await Assert.That(received).IsNotNull();
        _ = await Assert.That(received!.Message.Value).IsEqualTo(message.Payload);
        _ = await Assert.That(received.Message.Key).IsEqualTo(message.Id.ToString("D"));
    }

    [Test]
    public async Task SendBatchAsync_Delivers_all_messages_to_kafka_topic()
    {
        if (string.IsNullOrWhiteSpace(_bootstrapServers))
        {
            return;
        }

        const string batchTopic = "pulse-outbox-batch-tests";
        await EnsureTopicExistsAsync(_bootstrapServers, batchTopic);

        using var producer = BuildProducerAdapter(_bootstrapServers);
        using var admin = BuildAdminAdapter(_bootstrapServers);
        using var transport = new KafkaMessageTransport(producer, admin, new FixedTopicNameResolver(batchTopic));

        var messages = Enumerable
            .Range(0, 3)
            .Select(i => new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = $"Integration.Event.{i}",
                Payload = $$$"""{"index":{{{i}}}}""",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            })
            .ToArray();

        await transport.SendBatchAsync(messages);

        var received = await ConsumeManyMessagesAsync(_bootstrapServers, batchTopic, messages.Length);
        _ = await Assert.That(received.Count).IsEqualTo(messages.Length);
    }

    [Test]
    public async Task IsHealthyAsync_Returns_true_when_broker_is_reachable()
    {
        if (string.IsNullOrWhiteSpace(_bootstrapServers))
        {
            return;
        }

        using var producer = BuildProducerAdapter(_bootstrapServers);
        using var admin = BuildAdminAdapter(_bootstrapServers);
        using var transport = new KafkaMessageTransport(producer, admin, new FixedTopicNameResolver(TopicName));

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_Returns_false_when_broker_is_unreachable()
    {
        using var producer = BuildProducerAdapter("localhost:9099");
        using var admin = BuildAdminAdapter("localhost:9099");
        using var transport = new KafkaMessageTransport(producer, admin, new FixedTopicNameResolver(TopicName));

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsFalse();
    }

    public async ValueTask DisposeAsync()
    {
        if (_kafkaContainer is not null)
        {
            await _kafkaContainer.DisposeAsync();
        }
    }

    private static KafkaProducerAdapter BuildProducerAdapter(string bootstrapServers)
    {
        var config = new ProducerConfig { BootstrapServers = bootstrapServers, Acks = Acks.All };
        return new KafkaProducerAdapter(new ProducerBuilder<string, string>(config).Build());
    }

    private static KafkaAdminAdapter BuildAdminAdapter(string bootstrapServers)
    {
        var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
        return new KafkaAdminAdapter(new AdminClientBuilder(config).Build());
    }

    private static async Task EnsureTopicExistsAsync(string bootstrapServers, string topicName)
    {
        using var adminClient = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = bootstrapServers }
        ).Build();

        try
        {
            await adminClient.CreateTopicsAsync([
                new TopicSpecification
                {
                    Name = topicName,
                    NumPartitions = 1,
                    ReplicationFactor = 1,
                },
            ]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // Topic already exists – that's fine.
        }
    }

    private static async Task<ConsumeResult<string, string>?> ConsumeOneMessageAsync(
        string bootstrapServers,
        string topicName
    )
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"test-consumer-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topicName);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            return consumer.Consume(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            consumer.Unsubscribe();
        }
    }

    private static async Task<List<ConsumeResult<string, string>>> ConsumeManyMessagesAsync(
        string bootstrapServers,
        string topicName,
        int expectedCount
    )
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"test-consumer-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topicName);

        var results = new List<ConsumeResult<string, string>>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            while (results.Count < expectedCount)
            {
                var result = consumer.Consume(cts.Token);
                results.Add(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Return whatever we collected before the timeout.
        }
        finally
        {
            consumer.Unsubscribe();
        }

        return await Task.FromResult(results);
    }

    private sealed class FixedTopicNameResolver(string topic) : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => topic;
    }
}
