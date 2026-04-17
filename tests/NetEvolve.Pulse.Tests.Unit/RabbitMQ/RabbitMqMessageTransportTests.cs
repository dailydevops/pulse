namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

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
    public async Task Constructor_When_connectionAdapter_null_throws()
    {
        IRabbitMqConnectionAdapter connectionAdapter = null!;
        var topicNameResolver = new FakeTopicNameResolver();
        var options = CreateOptions();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            _ = new RabbitMqMessageTransport(connectionAdapter, topicNameResolver, options)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("connectionAdapter");
    }

    [Test]
    public async Task Constructor_When_topicNameResolver_null_throws()
    {
        var connectionAdapter = new FakeConnectionAdapter();
        ITopicNameResolver topicNameResolver = null!;
        var options = CreateOptions();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            _ = new RabbitMqMessageTransport(connectionAdapter, topicNameResolver, options)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("topicNameResolver");
    }

    [Test]
    public async Task Constructor_When_options_null_throws()
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        IOptions<RabbitMqTransportOptions> options = null!;

        var exception = Assert.Throws<ArgumentNullException>(() =>
            _ = new RabbitMqMessageTransport(connectionAdapter, topicNameResolver, options)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("options");
    }

    [Test]
    public async Task SendAsync_When_message_null_throws(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            transport.SendAsync(null!, cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("message");
    }

    [Test]
    public async Task SendAsync_Publishes_message_with_correct_properties(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver, exchangeName: "test-exchange");
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(1);
        var channel = connectionAdapter.CreatedChannels.Single();
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
    public async Task SendAsync_Reuses_open_channel(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);
        var message1 = CreateOutboxMessage();
        var message2 = CreateOutboxMessage();

        await transport.SendAsync(message1, cancellationToken).ConfigureAwait(false);
        await transport.SendAsync(message2, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(1);
        var channel = connectionAdapter.CreatedChannels.Single();
        _ = await Assert.That(channel.PublishCallCount).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_Creates_new_channel_when_previous_closed(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);
        var message1 = CreateOutboxMessage();
        var message2 = CreateOutboxMessage();

        await transport.SendAsync(message1, cancellationToken).ConfigureAwait(false);
        var firstChannel = connectionAdapter.CreatedChannels.Single();
        firstChannel.IsOpen = false; // Simulate channel closure

        await transport.SendAsync(message2, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(2);
        _ = await Assert.That(connectionAdapter.CreatedChannels.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_Uses_topic_name_resolver_for_routing_key(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver { ResolvedName = "custom-routing-key" };
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        var channel = connectionAdapter.CreatedChannels.Single();
        var publishCall = channel.PublishCalls.Single();
        _ = await Assert.That(publishCall.RoutingKey).IsEqualTo("custom-routing-key");
        _ = await Assert.That(topicNameResolver.ResolveCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task IsHealthyAsync_When_connection_not_open_returns_false(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter { IsOpen = false };
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_When_channel_not_created_returns_false(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter { IsOpen = true };
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_When_channel_not_open_returns_false(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter { IsOpen = true };
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        // Create a channel
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
        var channel = connectionAdapter.CreatedChannels.Single();
        channel.IsOpen = false;

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_When_connection_and_channel_open_returns_true(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter { IsOpen = true };
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        // Create a channel
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_When_exception_thrown_returns_false(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter { IsOpen = true, ThrowOnIsOpen = true };
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task Dispose_Disposes_channel_and_lock(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
        var channel = connectionAdapter.CreatedChannels.Single();

        transport.Dispose();

        _ = await Assert.That(channel.DisposeCalled).IsTrue();
    }

    [Test]
    public async Task Dispose_Is_idempotent(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(connectionAdapter, topicNameResolver);

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
        var channel = connectionAdapter.CreatedChannels.Single();

        transport.Dispose();
        transport.Dispose();

        _ = await Assert.That(channel.DisposeCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SendBatchAsync_When_messages_null_throws(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            transport.SendBatchAsync(null!, cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("messages");
    }

    [Test]
    public async Task SendBatchAsync_Publishes_all_messages_with_correct_properties(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver, exchangeName: "test-exchange");
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage(), CreateOutboxMessage() };

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(1);
        var channel = connectionAdapter.CreatedChannels.Single();
        _ = await Assert.That(channel.PublishCallCount).IsEqualTo(3);

        foreach (var publishCall in channel.PublishCalls)
        {
            _ = await Assert.That(publishCall.Exchange).IsEqualTo("test-exchange");
            _ = await Assert.That(publishCall.Mandatory).IsFalse();
            _ = await Assert.That(publishCall.Properties.ContentType).IsEqualTo("application/json");
        }
    }

    [Test]
    public async Task SendBatchAsync_Uses_single_channel_for_all_messages(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage(), CreateOutboxMessage() };

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(1);
        var channel = connectionAdapter.CreatedChannels.Single();
        _ = await Assert.That(channel.PublishCallCount).IsEqualTo(3);
    }

    [Test]
    public async Task SendBatchAsync_With_empty_collection_does_not_publish(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        await transport.SendBatchAsync([], cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(1);
        var channel = connectionAdapter.CreatedChannels.Single();
        _ = await Assert.That(channel.PublishCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task SendBatchAsync_Reuses_existing_channel(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        // First call creates a channel
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        // Second call reuses the same channel
        await transport
            .SendBatchAsync([CreateOutboxMessage(), CreateOutboxMessage()], cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(connectionAdapter.CreateChannelCallCount).IsEqualTo(1);
        var channel = connectionAdapter.CreatedChannels.Single();
        _ = await Assert.That(channel.PublishCallCount).IsEqualTo(3);
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

    private static RabbitMqMessageTransport CreateTransport(
        IRabbitMqConnectionAdapter connectionAdapter,
        ITopicNameResolver topicNameResolver,
        string exchangeName = "events"
    )
    {
        var options = CreateOptions(exchangeName);
        return new RabbitMqMessageTransport(connectionAdapter, topicNameResolver, options);
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

    private sealed class FakeConnectionAdapter : IRabbitMqConnectionAdapter
    {
        private bool _isOpen = true;

        public bool IsOpen
        {
            get
            {
                if (ThrowOnIsOpen)
                {
                    throw new InvalidOperationException("Connection check failed");
                }

                return _isOpen;
            }
            set => _isOpen = value;
        }

        public bool ThrowOnIsOpen { get; set; }

        public int CreateChannelCallCount { get; private set; }

        public List<FakeChannelAdapter> CreatedChannels { get; } = [];

        public Task<IRabbitMqChannelAdapter> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            CreateChannelCallCount++;
            var channel = new FakeChannelAdapter();
            CreatedChannels.Add(channel);
            return Task.FromResult<IRabbitMqChannelAdapter>(channel);
        }
    }

    private sealed class FakeChannelAdapter : IRabbitMqChannelAdapter
    {
        public bool IsOpen { get; set; } = true;

        public int PublishCallCount { get; private set; }

        public List<PublishCall> PublishCalls { get; } = [];

        public bool DisposeCalled { get; private set; }

        public int DisposeCallCount { get; private set; }

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

        public void Dispose()
        {
            DisposeCalled = true;
            DisposeCallCount++;
        }
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
