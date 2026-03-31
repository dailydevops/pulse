namespace NetEvolve.Pulse.Tests.Integration.RabbitMQ;

using System.Text;
using global::RabbitMQ.Client;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using Testcontainers.RabbitMq;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using TUnit.Core.Interfaces;

public sealed class RabbitMqTransportIntegrationTests : IAsyncDisposable, IAsyncInitializer
{
    private const string ExchangeName = "pulse-outbox-tests";
    private const string QueueName = "pulse-outbox-queue";
    private const string RoutingKey = "test.events";

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:3.11").WithCleanUp(true).Build();
    private IConnection? _connection;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var factory = new ConnectionFactory { Uri = new Uri(_container.GetConnectionString()) };
        _connection = await factory.CreateConnectionAsync();

        // Declare exchange and queue for testing
        var channel = await _connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: false);
        _ = await channel.QueueDeclareAsync(QueueName, durable: false, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(QueueName, ExchangeName, RoutingKey);
        await channel.CloseAsync();
    }

    [Test]
    public async Task SendAsync_Publishes_message_to_rabbitmq()
    {
        using var transport = CreateTransport();
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
    public async Task IsHealthyAsync_When_connected_returns_true()
    {
        using var transport = CreateTransport();

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
        var options = new RabbitMqTransportOptions { ExchangeName = ExchangeName };

        var adapter = new RabbitMqConnectionAdapter(_connection!);
        var topicNameResolverMock = Mock.Of<ITopicNameResolver>();
        _ = topicNameResolverMock.Resolve(Any()).Returns(RoutingKey);
        return new RabbitMqMessageTransport(adapter, topicNameResolverMock.Object, Options.Create(options));
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
            result.BasicProperties.Headers?.TryGetValue("eventType", out var et) == true && et is byte[] etBytes
                ? Encoding.UTF8.GetString(etBytes)
                : null;

        await channel.CloseAsync();

        return new ReceivedMessage(body, messageId, correlationId, eventType);
    }

    private sealed record ReceivedMessage(string Body, string? MessageId, string? CorrelationId, string? EventType);
}
