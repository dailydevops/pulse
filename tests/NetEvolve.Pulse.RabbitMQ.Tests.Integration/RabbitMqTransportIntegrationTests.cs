namespace NetEvolve.Pulse.RabbitMQ.Tests.Integration;

using System.Text;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using global::RabbitMQ.Client;
using Testcontainers.RabbitMq;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class RabbitMqTransportIntegrationTests : IAsyncDisposable
{
    private const string ExchangeName = "pulse-outbox-tests";
    private const string QueueName = "pulse-outbox-queue";
    private const string RoutingKey = "test.events";

    private RabbitMqContainer? _container;
    private IConnection? _connection;

    [Before(Test)]
    public async Task StartContainerAsync()
    {
        _container = new RabbitMqBuilder().WithCleanUp(true).Build();

        await _container.StartAsync();

        var factory = new ConnectionFactory { Uri = new Uri(_container.GetConnectionString()) };
        _connection = await factory.CreateConnectionAsync();

        // Declare exchange and queue for testing
        var channel = await _connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: false);
        await channel.QueueDeclareAsync(QueueName, durable: false, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(QueueName, ExchangeName, RoutingKey);
        await channel.CloseAsync();
    }

    [Test]
    public async Task SendAsync_Publishes_message_to_rabbitmq()
    {
        var transport = CreateTransport();
        var message = CreateOutboxMessage();

        await transport.SendAsync(message);

        var receivedMessage = await ConsumeMessageAsync();

        using (Assert.Multiple())
        {
            _ = await Assert.That(receivedMessage).IsNotNull();
            _ = await Assert.That(receivedMessage!.Body).IsEqualTo(message.Payload);
            _ = await Assert.That(receivedMessage.MessageId).IsEqualTo(message.Id.ToString());
            _ = await Assert.That(receivedMessage.CorrelationId).IsEqualTo(message.CorrelationId);
            _ = await Assert.That(receivedMessage.EventType).IsEqualTo(message.EventType);
        }
    }

    [Test]
    public async Task SendBatchAsync_Publishes_multiple_messages()
    {
        var transport = CreateTransport();
        var messages = Enumerable.Range(0, 3).Select(_ => CreateOutboxMessage()).ToArray();

        await transport.SendBatchAsync(messages);

        var receivedMessages = new List<ReceivedMessage>();
        for (var i = 0; i < messages.Length; i++)
        {
            var received = await ConsumeMessageAsync();
            if (received is not null)
            {
                receivedMessages.Add(received);
            }
        }

        _ = await Assert.That(receivedMessages.Count).IsEqualTo(messages.Length);
    }

    [Test]
    public async Task IsHealthyAsync_When_connected_returns_true()
    {
        var transport = CreateTransport();

        // Trigger connection by sending a message
        await transport.SendAsync(CreateOutboxMessage());

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_When_disposed_returns_false()
    {
        var transport = CreateTransport();

        // Trigger connection
        await transport.SendAsync(CreateOutboxMessage());

        transport.Dispose();

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task SendAsync_With_custom_routing_key_resolver()
    {
        var options = new RabbitMqTransportOptions
        {
            HostName = new Uri(_container!.GetConnectionString()).Host,
            Port = new Uri(_container.GetConnectionString()).Port,
            ExchangeName = ExchangeName,
            RoutingKeyResolver = message =>
            {
                var eventType = message.EventType.Split(',')[0].Split('.').Last();
                return $"custom.{eventType.ToLowerInvariant()}";
            },
        };

        // Bind queue to custom routing key pattern
        var channel = await _connection!.CreateChannelAsync();
        await channel.QueueBindAsync(QueueName, ExchangeName, "custom.#");
        await channel.CloseAsync();

        var transport = new RabbitMqMessageTransport(Options.Create(options));
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "MyApp.Events.OrderCreated, MyApp",
            Payload = """{"event":"test"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await transport.SendAsync(message);

        var received = await ConsumeMessageAsync();

        _ = await Assert.That(received).IsNotNull();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private RabbitMqMessageTransport CreateTransport()
    {
        var options = new RabbitMqTransportOptions
        {
            HostName = new Uri(_container!.GetConnectionString()).Host,
            Port = new Uri(_container.GetConnectionString()).Port,
            ExchangeName = ExchangeName,
            RoutingKey = RoutingKey,
        };

        return new RabbitMqMessageTransport(Options.Create(options));
    }

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = "Integration.Event",
            Payload = """{"event":"integration"}""",
            CorrelationId = "corr-123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 1,
        };

    private async Task<ReceivedMessage?> ConsumeMessageAsync()
    {
        var channel = await _connection!.CreateChannelAsync();
        var result = await channel.BasicGetAsync(QueueName, autoAck: true);

        if (result is null)
        {
            await channel.CloseAsync();
            return null;
        }

        var body = Encoding.UTF8.GetString(result.Body.ToArray());
        var messageId = result.BasicProperties.MessageId;
        var correlationId = result.BasicProperties.CorrelationId;
        var eventType =
            result.BasicProperties.Headers?.TryGetValue("eventType", out var et) == true
                ? Encoding.UTF8.GetString((byte[])et)
                : null;

        await channel.CloseAsync();

        return new ReceivedMessage(body, messageId, correlationId, eventType);
    }

    private sealed record ReceivedMessage(string Body, string? MessageId, string? CorrelationId, string? EventType);
}
