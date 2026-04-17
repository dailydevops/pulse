namespace NetEvolve.Pulse.Tests.Integration.RabbitMQ;

using System.Text;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
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
public sealed class RabbitMqMessageTransportIntegrationTests(RabbitMqContainerFixture containerFixture)
    : IAsyncDisposable
{
    private const string ExchangeName = "pulse.integration.test";

    private IConnection? _connection;
    private IChannel? _adminChannel;

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
    public async Task IsHealthyAsync_Before_first_send_returns_false(CancellationToken cancellationToken)
    {
        var (connection, _) = await GetConnectionAndChannelAsync(cancellationToken).ConfigureAwait(false);

        var adapter = new RabbitMqConnectionAdapter(connection);
        using var transport = CreateTransport(adapter);

        // No sends yet — channel has not been created
        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    private static RabbitMqMessageTransport CreateTransport(IRabbitMqConnectionAdapter adapter) =>
        new(
            adapter,
            new SimpleTopicNameResolver(),
            Options.Create(new RabbitMqTransportOptions { ExchangeName = ExchangeName })
        );

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
        cts.Token.Register(() => tcs.TrySetResult(null));

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            tcs.TrySetResult(ea);
            return Task.CompletedTask;
        };

        await channel
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
        cts.Token.Register(() => tcs.TrySetResult(false));

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            received.Add(ea);
            if (received.Count >= expectedCount)
            {
                tcs.TrySetResult(true);
            }

            return Task.CompletedTask;
        };

        await channel
            .BasicConsumeAsync(queueName, autoAck: true, consumer: consumer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await tcs.Task.ConfigureAwait(false);
        return received;
    }

    private sealed class SimpleTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => message.EventType.Name;
    }

    private sealed record IntegrationTestEvent;
}
