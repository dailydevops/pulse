namespace NetEvolve.Pulse.Tests.Integration.RabbitMQ;

using System.Text;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="RabbitMqMessageTransport"/> against a real RabbitMQ broker.
/// </summary>
[ClassDataSource<RabbitMqContainerFixture>(Shared = SharedType.PerTestSession)]
[TestGroup("RabbitMQ")]
[Timeout(120_000)]
[NotInParallel]
public sealed class RabbitMqMessageTransportIntegrationTests(RabbitMqContainerFixture containerFixture)
    : IAsyncDisposable
{
    private const string ExchangeName = "pulse.integration.test";

    private IConnection? _connection;
    private IChannel? _adminChannel;
    private RabbitMqChannelPool? _channelPool;

    private async Task<(IConnection Connection, IChannel AdminChannel)> GetConnectionAndChannelAsync(
        CancellationToken cancellationToken
    )
    {
        if (_connection is not null && _adminChannel is not null)
        {
            return (_connection, _adminChannel);
        }

        var factory = new ConnectionFactory { Uri = new Uri(containerFixture.ConnectionString) };
        _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _adminChannel = await _connection
            .CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _adminChannel
            .ExchangeDeclareAsync(
                ExchangeName,
                ExchangeType.Fanout,
                durable: false,
                autoDelete: true,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return (_connection, _adminChannel);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _channelPool?.Dispose();

        if (_adminChannel is not null)
        {
            await _adminChannel.CloseAsync().ConfigureAwait(false);
            _adminChannel.Dispose();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task SendAsync_Publishes_message_to_exchange(CancellationToken cancellationToken)
    {
        var (connection, adminChannel) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var queueName = await adminChannel
            .QueueDeclareAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await adminChannel
            .QueueBindAsync(
                queueName.QueueName,
                ExchangeName,
                routingKey: string.Empty,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var adapter = new RabbitMqConnectionAdapter(connection);
        using var transport = CreateTransport(adapter);
        var outboxMessage = CreateOutboxMessage();

        await transport.SendAsync(outboxMessage, cancellationToken).ConfigureAwait(false);

        var received = await ConsumeOneMessageAsync(adminChannel, queueName.QueueName, cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(received).IsNotNull();
            var body = Encoding.UTF8.GetString(received!.Body.ToArray());
            _ = await Assert.That(body).IsEqualTo(outboxMessage.Payload);
            _ = await Assert.That(received.BasicProperties.MessageId).IsEqualTo(outboxMessage.Id.ToString());
            _ = await Assert.That(received.BasicProperties.ContentType).IsEqualTo("application/json");
        }
    }

    [Test]
    public async Task SendBatchAsync_Publishes_all_messages_to_exchange(CancellationToken cancellationToken)
    {
        const int messageCount = 5;
        var (connection, adminChannel) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var queueName = await adminChannel
            .QueueDeclareAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await adminChannel
            .QueueBindAsync(
                queueName.QueueName,
                ExchangeName,
                routingKey: string.Empty,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var adapter = new RabbitMqConnectionAdapter(connection);
        using var transport = CreateTransport(adapter);
        var messages = Enumerable.Range(0, messageCount).Select(_ => CreateOutboxMessage()).ToList();

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        var receivedMessages = await ConsumeManyMessagesAsync(
                adminChannel,
                queueName.QueueName,
                messageCount,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(receivedMessages.Count).IsEqualTo(messageCount);
    }

    [Test]
    public async Task IsHealthyAsync_When_connection_open_returns_true(CancellationToken cancellationToken)
    {
        var (connection, _) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var adapter = new RabbitMqConnectionAdapter(connection);
        using var transport = CreateTransport(adapter);

        // Trigger channel creation by sending a message
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_Before_first_send_returns_true_when_connection_open(
        CancellationToken cancellationToken
    )
    {
        var (connection, _) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var adapter = new RabbitMqConnectionAdapter(connection);
        using var transport = CreateTransport(adapter);

        // No sends yet — no channel has been rented from the pool. Health now reflects the
        // underlying connection/pool state rather than the existence of a particular channel,
        // so this must report healthy as long as the connection is open.
        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_After_dispose_returns_false(CancellationToken cancellationToken)
    {
        var (connection, _) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var adapter = new RabbitMqConnectionAdapter(connection);
        var transport = CreateTransport(adapter);

        // Establish a channel before disposing, to prove disposal short-circuits the health check
        // rather than merely reporting an unopened channel as unhealthy.
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
        transport.Dispose();

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task Dispose_Called_twice_is_idempotent(CancellationToken cancellationToken)
    {
        var (connection, _) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var adapter = new RabbitMqConnectionAdapter(connection);
        var transport = CreateTransport(adapter);
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        transport.Dispose();

        // Second Dispose() call must be a safe no-op, not throw or double-dispose the channel.
        transport.Dispose();
    }

    [Test]
    public async Task SendAsync_Called_twice_reuses_open_channel(CancellationToken cancellationToken)
    {
        var (connection, adminChannel) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var queueName = await adminChannel
            .QueueDeclareAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await adminChannel
            .QueueBindAsync(
                queueName.QueueName,
                ExchangeName,
                routingKey: string.Empty,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var adapter = new RabbitMqConnectionAdapter(connection);
        using var transport = CreateTransport(adapter);

        // First call creates the channel; the second call must hit the already-open fast path
        // in EnsureChannelAsync instead of re-entering the initialization lock.
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        var receivedMessages = await ConsumeManyMessagesAsync(
                adminChannel,
                queueName.QueueName,
                expectedCount: 2,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(receivedMessages.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendAsync_Called_concurrently_initializes_channel_exactly_once(
        CancellationToken cancellationToken
    )
    {
        var (connection, adminChannel) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var queueName = await adminChannel
            .QueueDeclareAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await adminChannel
            .QueueBindAsync(
                queueName.QueueName,
                ExchangeName,
                routingKey: string.Empty,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var adapter = new RabbitMqConnectionAdapter(connection);
        using var transport = CreateTransport(adapter);

        const int concurrentSends = 10;

        // Fire many sends concurrently against a transport with no channel yet, to exercise the
        // double-checked locking re-check branch inside EnsureChannelAsync.
        var tasks = Enumerable
            .Range(0, concurrentSends)
            .Select(_ => transport.SendAsync(CreateOutboxMessage(), cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var receivedMessages = await ConsumeManyMessagesAsync(
                adminChannel,
                queueName.QueueName,
                expectedCount: concurrentSends,
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(receivedMessages.Count).IsEqualTo(concurrentSends);
    }

    [Test]
    public async Task UseRabbitMqTransport_Registers_transport_and_connection_adapter(
        CancellationToken cancellationToken
    )
    {
        var (connection, _) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var services = new ServiceCollection();
        _ = services.AddSingleton(connection);
        _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _ = services.AddPulse(config => config.UseRabbitMqTransport(o => o.ExchangeName = ExchangeName));

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            // RabbitMqMessageTransport itself has an internal constructor and cannot be resolved by
            // the container's default reflection-based activator; verify the surrounding registration
            // (connection adapter factory + options) that UseRabbitMqTransport is responsible for instead.
            var adapter = provider.GetRequiredService<IRabbitMqConnectionAdapter>();
            _ = await Assert.That(adapter.IsOpen).IsTrue();

            var options = provider.GetRequiredService<IOptions<RabbitMqTransportOptions>>();
            _ = await Assert.That(options.Value.ExchangeName).IsEqualTo(ExchangeName);

            var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(RabbitMqMessageTransport));
        }
    }

    [Test]
    public async Task UseRabbitMqTransport_Replaces_existing_transport(CancellationToken cancellationToken)
    {
        var (connection, _) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var services = new ServiceCollection();
        _ = services.AddSingleton(connection);
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config => config.UseRabbitMqTransport(o => o.ExchangeName = ExchangeName));

        var descriptors = services.Where(d => d.ServiceType == typeof(IMessageTransport)).ToList();

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptors.Count).IsEqualTo(1);
            _ = await Assert.That(descriptors[0].ImplementationType).IsEqualTo(typeof(RabbitMqMessageTransport));
        }
    }

    // The channel pool is created lazily and shared across the tests in this fixture (they
    // all operate against the same underlying connection) and is disposed together with the
    // connection in DisposeAsync.
    private RabbitMqMessageTransport CreateTransport(IRabbitMqConnectionAdapter adapter)
    {
        _channelPool ??= new RabbitMqChannelPool(adapter, new RabbitMqTransportOptions().MaxChannelPoolSize);

        return new RabbitMqMessageTransport(
            _channelPool,
            new SimpleTopicNameResolver(),
            Options.Create(new RabbitMqTransportOptions { ExchangeName = ExchangeName })
        );
    }

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

    private static async Task<BasicDeliverEventArgs?> ConsumeOneMessageAsync(
        IChannel channel,
        string queueName,
        CancellationToken cancellationToken
    )
    {
        var tcs = new TaskCompletionSource<BasicDeliverEventArgs?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        _ = cts.Token.Register(() => tcs.TrySetResult(null));

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (sender, ea) =>
        {
            _ = tcs.TrySetResult(ea);
            return Task.CompletedTask;
        };

        _ = await channel
            .BasicConsumeAsync(queueName, autoAck: true, consumer: consumer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return await tcs.Task.ConfigureAwait(false);
    }

    private static async Task<List<BasicDeliverEventArgs>> ConsumeManyMessagesAsync(
        IChannel channel,
        string queueName,
        int expectedCount,
        CancellationToken cancellationToken
    )
    {
        var received = new List<BasicDeliverEventArgs>();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        _ = cts.Token.Register(() => tcs.TrySetResult(false));

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (sender, ea) =>
        {
            received.Add(ea);
            if (received.Count >= expectedCount)
            {
                _ = tcs.TrySetResult(true);
            }

            return Task.CompletedTask;
        };

        _ = await channel
            .BasicConsumeAsync(queueName, autoAck: true, consumer: consumer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _ = await tcs.Task.ConfigureAwait(false);
        return received;
    }

    private sealed class SimpleTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => message.EventType.Name;
    }

    private sealed record IntegrationTestEvent;

#pragma warning disable CA1812 // Avoid uninstantiated internal classes - instantiated via DI container
    private sealed class DummyTransport : IMessageTransport
#pragma warning restore CA1812
    {
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendBatchAsync(
            IEnumerable<OutboxMessage> messages,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}
