namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using System.Linq;
using System.Text;
using global::RabbitMQ.Client;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("RabbitMQ")]
public sealed class RabbitMqMessageTransportTests
{
    [Test]
    public async Task Constructor_When_channelPool_null_throws()
    {
        IRabbitMqChannelPool channelPool = null!;
        var topicNameResolver = new FakeTopicNameResolver();
        var options = CreateOptions();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            _ = new RabbitMqMessageTransport(channelPool, topicNameResolver, options)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception.ParamName).IsEqualTo("channelPool");
    }

    [Test]
    public async Task Constructor_When_topicNameResolver_null_throws()
    {
        var channelPool = new FakeChannelPool();
        ITopicNameResolver topicNameResolver = null!;
        var options = CreateOptions();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            _ = new RabbitMqMessageTransport(channelPool, topicNameResolver, options)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception.ParamName).IsEqualTo("topicNameResolver");
    }

    [Test]
    public async Task Constructor_When_options_null_throws()
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        IOptions<RabbitMqTransportOptions> options = null!;

        var exception = Assert.Throws<ArgumentNullException>(() =>
            _ = new RabbitMqMessageTransport(channelPool, topicNameResolver, options)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception.ParamName).IsEqualTo("options");
    }

    [Test]
    public async Task SendAsync_When_message_null_throws(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            transport.SendAsync(null!, cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("message");
    }

    [Test]
    public async Task SendAsync_Publishes_message_with_correct_properties(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver, exchangeName: "test-exchange");
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(channelPool.RentCallCount).IsEqualTo(1);
        var channel = channelPool.RentedChannels.Single();
        _ = await Assert.That(channel.PublishCallCount).IsEqualTo(1);

        var publishCall = channel.PublishCalls.Single();
        using (Assert.Multiple())
        {
            _ = await Assert.That(publishCall.Exchange).IsEqualTo("test-exchange");
            _ = await Assert.That(publishCall.RoutingKey).IsEqualTo(outboxMessage.EventType.Name);
            _ = await Assert.That(publishCall.Mandatory).IsFalse();

            var props = publishCall.Properties;
            _ = await Assert.That(props.MessageId).IsEqualTo(outboxMessage.Id.ToString());
            _ = await Assert.That(props.CorrelationId).IsEqualTo(outboxMessage.CorrelationId);
            _ = await Assert.That(props.ContentType).IsEqualTo("application/json");
            _ = await Assert
                .That(props.Headers!["eventType"])
                .IsEqualTo(outboxMessage.EventType.ToOutboxEventTypeName());
            _ = await Assert.That(props.Headers!["retryCount"]).IsEqualTo(outboxMessage.RetryCount);

            var bodyText = Encoding.UTF8.GetString(publishCall.Body.ToArray());
            _ = await Assert.That(bodyText).IsEqualTo(outboxMessage.Payload);
        }
    }

    [Test]
    public async Task SendAsync_Returns_channel_to_pool_after_publish(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);
        var message1 = CreateOutboxMessage();
        var message2 = CreateOutboxMessage();

        await transport.SendAsync(message1, cancellationToken).ConfigureAwait(false);
        await transport.SendAsync(message2, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(channelPool.RentCallCount).IsEqualTo(2);
        _ = await Assert.That(channelPool.ReturnCallCount).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_Returns_channel_even_when_publish_throws(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);

        channelPool.NextRentedChannelThrowsOnPublish = true;

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.SendAsync(CreateOutboxMessage(), cancellationToken)
        );

        _ = await Assert.That(channelPool.RentCallCount).IsEqualTo(1);
        _ = await Assert.That(channelPool.ReturnCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SendBatchAsync_Rents_single_channel_and_publishes_sequentially(
        CancellationToken cancellationToken
    )
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver, exchangeName: "batch-ex");

        var messages = Enumerable.Range(0, 5).Select(_ => CreateOutboxMessage()).ToArray();
        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        // Exactly one channel rented for the whole batch and returned afterwards.
        _ = await Assert.That(channelPool.RentCallCount).IsEqualTo(1);
        _ = await Assert.That(channelPool.ReturnCallCount).IsEqualTo(1);
        var channel = channelPool.RentedChannels.Single();
        _ = await Assert.That(channel.PublishCallCount).IsEqualTo(messages.Length);
        // All routed to the configured exchange.
        _ = await Assert.That(channel.PublishCalls.All(p => p.Exchange == "batch-ex")).IsTrue();
    }

    [Test]
    public async Task SendBatchAsync_Returns_channel_even_when_publish_throws(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);

        channelPool.NextRentedChannelThrowsOnPublish = true;

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.SendBatchAsync([CreateOutboxMessage()], cancellationToken)
        );

        _ = await Assert.That(channelPool.RentCallCount).IsEqualTo(1);
        _ = await Assert.That(channelPool.ReturnCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_Uses_topic_name_resolver_for_routing_key(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver { ResolvedName = "custom-routing-key" };
        using var transport = CreateTransport(channelPool, topicNameResolver);
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        var channel = channelPool.RentedChannels.Single();
        var publishCall = channel.PublishCalls.Single();
        _ = await Assert.That(publishCall.RoutingKey).IsEqualTo("custom-routing-key");
        _ = await Assert.That(topicNameResolver.ResolveCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task IsHealthyAsync_Delegates_to_channel_pool(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool { Healthy = false };
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
        _ = await Assert.That(channelPool.IsHealthyCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task IsHealthyAsync_When_pool_reports_healthy_returns_true(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool { Healthy = true };
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_When_exception_thrown_returns_false(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool { ThrowOnIsHealthyAsync = true };
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task Dispose_Is_idempotent()
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(channelPool, topicNameResolver);

        transport.Dispose();
        transport.Dispose();

        // Dispose does not touch the pool (owned/disposed separately by DI); it must
        // simply be safe to call multiple times.
        await Task.CompletedTask;
    }

    // ── Post-dispose contract (DEEP-G-01) ─────────────────────────────────────

    [Test]
    public async Task SendAsync_After_Dispose_Throws_ObjectDisposedException(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(channelPool, topicNameResolver);

        transport.Dispose();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            transport.SendAsync(CreateOutboxMessage(), cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task SendBatchAsync_After_Dispose_Throws_ObjectDisposedException(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(channelPool, topicNameResolver);

        transport.Dispose();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            transport.SendBatchAsync([CreateOutboxMessage()], cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task IsHealthyAsync_After_Dispose_Returns_false(CancellationToken cancellationToken)
    {
        // Health probes typically run during shutdown; disposed transports must report unhealthy
        // rather than throwing so the probe path stays observable.
        var channelPool = new FakeChannelPool { Healthy = true };
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(channelPool, topicNameResolver);

        transport.Dispose();

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task SendAsync_When_message_null_throws_even_after_dispose(CancellationToken cancellationToken)
    {
        // ArgumentNullException must take precedence over ObjectDisposedException because
        // the order of checks pins the public contract for callers passing bad arguments.
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(channelPool, topicNameResolver);

        transport.Dispose();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            transport.SendAsync(null!, cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("message");
    }

    [Test]
    public async Task SendBatchAsync_When_messages_null_throws(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            transport.SendBatchAsync(null!, cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("messages");
    }

    [Test]
    public async Task SendBatchAsync_Publishes_all_messages_with_correct_properties(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver, exchangeName: "test-exchange");
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage(), CreateOutboxMessage() };

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(channelPool.RentCallCount).IsEqualTo(1);
        var channel = channelPool.RentedChannels.Single();
        _ = await Assert.That(channel.PublishCallCount).IsEqualTo(3);

        foreach (var publishCall in channel.PublishCalls)
        {
            _ = await Assert.That(publishCall.Exchange).IsEqualTo("test-exchange");
            _ = await Assert.That(publishCall.Mandatory).IsFalse();
            _ = await Assert.That(publishCall.Properties.ContentType).IsEqualTo("application/json");
        }
    }

    [Test]
    public async Task SendBatchAsync_With_empty_collection_does_not_publish(CancellationToken cancellationToken)
    {
        var channelPool = new FakeChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);

        await transport.SendBatchAsync([], cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(channelPool.RentCallCount).IsEqualTo(1);
        _ = await Assert.That(channelPool.ReturnCallCount).IsEqualTo(1);
        var channel = channelPool.RentedChannels.Single();
        _ = await Assert.That(channel.PublishCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Options_ExchangeName_can_be_configured()
    {
        var options = new RabbitMqTransportOptions { ExchangeName = "test-exchange" };

        _ = await Assert.That(options.ExchangeName).IsEqualTo("test-exchange");
    }

    [Test]
    public async Task Options_Default_ExchangeName_is_empty_string()
    {
        var options = new RabbitMqTransportOptions();

        _ = await Assert.That(options.ExchangeName).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Options_Default_MaxChannelPoolSize_is_ten()
    {
        var options = new RabbitMqTransportOptions();

        _ = await Assert.That(options.MaxChannelPoolSize).IsEqualTo(10);
    }

    [Test]
    public async Task Options_MaxChannelPoolSize_can_be_configured()
    {
        var options = new RabbitMqTransportOptions { MaxChannelPoolSize = 25 };

        _ = await Assert.That(options.MaxChannelPoolSize).IsEqualTo(25);
    }

    private static RabbitMqMessageTransport CreateTransport(
        IRabbitMqChannelPool channelPool,
        ITopicNameResolver topicNameResolver,
        string exchangeName = "events"
    )
    {
        var options = CreateOptions(exchangeName);
        return new RabbitMqMessageTransport(channelPool, topicNameResolver, options);
    }

    private static IOptions<RabbitMqTransportOptions> CreateOptions(string exchangeName = "events") =>
        Options.Create(new RabbitMqTransportOptions { ExchangeName = exchangeName });

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestRabbitMqEvent),
            Payload = """{"event":"sample"}""",
            CorrelationId = "corr-123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 1,
            ProcessedAt = DateTimeOffset.UtcNow,
        };

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        public string ResolvedName { get; set; } = nameof(TestRabbitMqEvent);

        public int ResolveCallCount { get; private set; }

        public string Resolve(OutboxMessage message)
        {
            ResolveCallCount++;
            return ResolvedName;
        }
    }

    private sealed record TestRabbitMqEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class FakeChannelPool : IRabbitMqChannelPool
    {
        public int RentCallCount { get; private set; }

        public int ReturnCallCount { get; private set; }

        public int IsHealthyCallCount { get; private set; }

        public bool Healthy { get; set; } = true;

        public bool ThrowOnIsHealthyAsync { get; set; }

        public bool NextRentedChannelThrowsOnPublish { get; set; }

        public List<FakeChannelAdapter> RentedChannels { get; } = [];

        public ValueTask<IRabbitMqChannelAdapter> RentAsync(CancellationToken cancellationToken)
        {
            RentCallCount++;
            var channel = new FakeChannelAdapter { ThrowsOnPublish = NextRentedChannelThrowsOnPublish };
            RentedChannels.Add(channel);
            return ValueTask.FromResult<IRabbitMqChannelAdapter>(channel);
        }

        public void Return(IRabbitMqChannelAdapter channel) => ReturnCallCount++;

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
        {
            IsHealthyCallCount++;
            if (ThrowOnIsHealthyAsync)
            {
                throw new InvalidOperationException("Health check failed");
            }

            return Task.FromResult(Healthy);
        }
    }

    private sealed class FakeChannelAdapter : IRabbitMqChannelAdapter
    {
        public bool IsOpen { get; set; } = true;

        public bool ThrowsOnPublish { get; set; }

        public int PublishCallCount { get; private set; }

        public List<PublishCall> PublishCalls { get; } = [];

        public ValueTask BasicPublishAsync<TProperties>(
            string exchange,
            string routingKey,
            bool mandatory,
            TProperties basicProperties,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default
        )
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader
        {
            if (ThrowsOnPublish)
            {
                throw new InvalidOperationException("Publish failed");
            }

            PublishCallCount++;
            PublishCalls.Add(
                new PublishCall
                {
                    Exchange = exchange,
                    RoutingKey = routingKey,
                    Mandatory = mandatory,
                    Properties = ExtractProperties(basicProperties),
                    Body = body,
                }
            );
            return ValueTask.CompletedTask;
        }

        private static BasicProperties ExtractProperties<TProperties>(TProperties props)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader
        {
            var result = new BasicProperties
            {
                MessageId = props.MessageId,
                CorrelationId = props.CorrelationId,
                ContentType = props.ContentType,
                Timestamp = props.Timestamp,
            };

            if (props.Headers is not null)
            {
                result.Headers = new Dictionary<string, object?>(props.Headers, StringComparer.Ordinal);
            }

            return result;
        }

        public void Dispose() { }
    }

    private sealed record PublishCall
    {
        public required string Exchange { get; init; }
        public required string RoutingKey { get; init; }
        public required bool Mandatory { get; init; }
        public required BasicProperties Properties { get; init; }
        public required ReadOnlyMemory<byte> Body { get; init; }
    }
}
