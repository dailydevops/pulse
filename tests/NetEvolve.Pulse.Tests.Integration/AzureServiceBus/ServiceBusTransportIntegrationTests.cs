namespace NetEvolve.Pulse.Tests.Integration.AzureServiceBus;

using System.Globalization;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class ServiceBusTransportIntegrationTests : IAsyncDisposable
{
    private const string QueueName = "pulse-outbox-tests";
    private IContainer? _emulatorContainer;
    private string? _connectionString;

    [Before(Test)]
    public async Task EnsureConnectionAsync()
    {
        _connectionString = GetConnectionStringFromEnvironment();
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            return;
        }

        var emulatorImage = Environment.GetEnvironmentVariable("SERVICEBUS_EMULATOR_IMAGE");
        var emulatorConnectionString = Environment.GetEnvironmentVariable("SERVICEBUS_EMULATOR_CONNECTIONSTRING");
        if (string.IsNullOrWhiteSpace(emulatorImage) || string.IsNullOrWhiteSpace(emulatorConnectionString))
        {
            return;
        }

        _emulatorContainer = new ContainerBuilder(emulatorImage).WithCleanUp(true).WithPortBinding(5672, true).Build();

        await _emulatorContainer.StartAsync();
        _connectionString = emulatorConnectionString.Replace(
            "5672",
            _emulatorContainer.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal
        );
    }

    [Test]
    public async Task SendAsync_Delivers_message_to_service_bus_queue()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return;
        }

        var adminClient = new ServiceBusAdministrationClient(_connectionString);
        if (!await adminClient.QueueExistsAsync(QueueName))
        {
            _ = await adminClient.CreateQueueAsync(QueueName);
        }

        await using var client = new ServiceBusClient(_connectionString);
        var topicNameResolver = new SimpleTopicNameResolver(QueueName);
        await using var transport = new AzureServiceBusMessageTransport(
            client,
            topicNameResolver,
            Options.Create(
                new AzureServiceBusTransportOptions { ConnectionString = _connectionString, EnableBatching = true }
            )
        );

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(IntegrationTestEvent),
            Payload = """{"event":"integration"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await transport.SendAsync(message);

        var receiver = client.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete }
        );
        var received = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));

        _ = await Assert.That(received).IsNotNull();
        _ = await Assert.That(received!.Body.ToString()).IsEqualTo(message.Payload);
        _ = await Assert.That(received.Subject).IsEqualTo(message.EventType.AssemblyQualifiedName);
    }

    public async ValueTask DisposeAsync()
    {
        if (_emulatorContainer is not null)
        {
            await _emulatorContainer.DisposeAsync();
        }
    }

    private static string? GetConnectionStringFromEnvironment() =>
        Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTIONSTRING");

    private sealed class SimpleTopicNameResolver : ITopicNameResolver
    {
        private readonly string _topicName;

        public SimpleTopicNameResolver(string topicName) => _topicName = topicName;

        public string Resolve(OutboxMessage message) => _topicName;
    }

    private sealed record IntegrationTestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
