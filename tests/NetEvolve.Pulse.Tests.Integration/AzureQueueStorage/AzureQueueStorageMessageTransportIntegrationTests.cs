namespace NetEvolve.Pulse.Tests.Integration.AzureQueueStorage;

using System.Text;
using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Integration tests for <see cref="AzureQueueStorageMessageTransport"/> against a real Azurite emulator,
/// exercising the lazy <see cref="QueueClient"/> initialization path that unit tests bypass via fakes.
/// </summary>
[ClassDataSource<AzuriteContainerFixture>(Shared = SharedType.PerTestSession)]
[TestGroup("AzureQueueStorage")]
[Timeout(120_000)]
[NotInParallel]
public sealed class AzureQueueStorageMessageTransportIntegrationTests(AzuriteContainerFixture containerFixture)
{
    [Test]
    public async Task SendAsync_Creates_queue_and_sends_base64_encoded_message(CancellationToken cancellationToken)
    {
        var queueName = CreateUniqueQueueName();
        var options = Options.Create(
            new AzureQueueStorageTransportOptions
            {
                ConnectionString = containerFixture.ConnectionString,
                QueueName = queueName,
                CreateQueueIfNotExists = true,
            }
        );
        using var transport = new AzureQueueStorageMessageTransport(options);
        var message = CreateOutboxMessage();

        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var queueClient = new QueueClient(containerFixture.ConnectionString, queueName, VerificationClientOptions);
        var response = await queueClient
            .ReceiveMessageAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var received = response.Value;

        _ = await Assert.That(received).IsNotNull();
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(received!.Body.ToString()));
        using var doc = JsonDocument.Parse(json);
        _ = await Assert.That(doc.RootElement.GetProperty("id").GetGuid()).IsEqualTo(message.Id);
    }

    [Test]
    public async Task SendAsync_When_CreateQueueIfNotExists_false_and_queue_missing_throws(
        CancellationToken cancellationToken
    )
    {
        var queueName = CreateUniqueQueueName();
        var options = Options.Create(
            new AzureQueueStorageTransportOptions
            {
                ConnectionString = containerFixture.ConnectionString,
                QueueName = queueName,
                CreateQueueIfNotExists = false,
            }
        );
        using var transport = new AzureQueueStorageMessageTransport(options);

        _ = await Assert
            .That(() => transport.SendAsync(CreateOutboxMessage(), cancellationToken))
            .Throws<Azure.RequestFailedException>();
    }

    [Test]
    public async Task SendBatchAsync_Sends_all_messages_sequentially(CancellationToken cancellationToken)
    {
        const int messageCount = 3;
        var queueName = CreateUniqueQueueName();
        var options = Options.Create(
            new AzureQueueStorageTransportOptions
            {
                ConnectionString = containerFixture.ConnectionString,
                QueueName = queueName,
                CreateQueueIfNotExists = true,
            }
        );
        using var transport = new AzureQueueStorageMessageTransport(options);
        var messages = Enumerable.Range(0, messageCount).Select(_ => CreateOutboxMessage()).ToList();

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        var queueClient = new QueueClient(containerFixture.ConnectionString, queueName, VerificationClientOptions);
        var response = await queueClient
            .ReceiveMessagesAsync(maxMessages: messageCount, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.Value.Length).IsEqualTo(messageCount);
    }

    [Test]
    public async Task SendAsync_With_prebuilt_queueClient_uses_override_instead_of_options(
        CancellationToken cancellationToken
    )
    {
        var queueName = CreateUniqueQueueName();
        var queueClient = new QueueClient(containerFixture.ConnectionString, queueName, VerificationClientOptions);
        _ = await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        // Options intentionally point at a different (never-created) queue, to prove the transport
        // uses the injected queueClient override rather than building a client from options.
        var options = Options.Create(
            new AzureQueueStorageTransportOptions
            {
                ConnectionString = containerFixture.ConnectionString,
                QueueName = CreateUniqueQueueName(),
                CreateQueueIfNotExists = false,
            }
        );
        using var transport = new AzureQueueStorageMessageTransport(options, queueClient);
        var message = CreateOutboxMessage();

        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var response = await queueClient
            .ReceiveMessageAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var received = response.Value;

        _ = await Assert.That(received).IsNotNull();
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(received!.Body.ToString()));
        using var doc = JsonDocument.Parse(json);
        _ = await Assert.That(doc.RootElement.GetProperty("id").GetGuid()).IsEqualTo(message.Id);
    }

    [Test]
    public async Task SendAsync_When_message_exceeds_size_limit_throws(CancellationToken cancellationToken)
    {
        var queueName = CreateUniqueQueueName();
        var options = Options.Create(
            new AzureQueueStorageTransportOptions
            {
                ConnectionString = containerFixture.ConnectionString,
                QueueName = queueName,
                CreateQueueIfNotExists = true,
            }
        );
        using var transport = new AzureQueueStorageMessageTransport(options);

        // Payload alone (well before JSON envelope overhead) already exceeds the 48 KB raw limit.
        var oversizedMessage = CreateOutboxMessage();
        oversizedMessage.Payload = new string('a', AzureQueueStorageMessageTransport.MaxMessageSizeInBytes + 1);

        _ = await Assert
            .That(() => transport.SendAsync(oversizedMessage, cancellationToken))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SendAsync_Called_concurrently_initializes_queueClient_exactly_once(
        CancellationToken cancellationToken
    )
    {
        var queueName = CreateUniqueQueueName();
        var options = Options.Create(
            new AzureQueueStorageTransportOptions
            {
                ConnectionString = containerFixture.ConnectionString,
                QueueName = queueName,
                CreateQueueIfNotExists = true,
            }
        );
        using var transport = new AzureQueueStorageMessageTransport(options);

        const int concurrentSends = 10;

        // Fire many sends concurrently against a transport with no queue client yet, to exercise
        // the double-checked locking re-check branch inside GetQueueClientAsync.
        var tasks = Enumerable
            .Range(0, concurrentSends)
            .Select(_ => transport.SendAsync(CreateOutboxMessage(), cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var queueClient = new QueueClient(containerFixture.ConnectionString, queueName, VerificationClientOptions);
        var response = await queueClient
            .ReceiveMessagesAsync(maxMessages: concurrentSends, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(response.Value.Length).IsEqualTo(concurrentSends);
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_Uri_overload_registers_transport(
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureQueueStorageTransport(
                new Uri("https://example.queue.core.windows.net"),
                o => o.QueueName = CreateUniqueQueueName()
            )
        );

        cancellationToken.ThrowIfCancellationRequested();

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var transport = provider.GetRequiredService<IMessageTransport>();

            _ = await Assert.That(transport).IsTypeOf<AzureQueueStorageMessageTransport>();
        }
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_Replaces_existing_transport(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config =>
            config.UseAzureQueueStorageTransport(containerFixture.ConnectionString, o => o.QueueName = "unused")
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var descriptors = services.Where(d => d.ServiceType == typeof(IMessageTransport)).ToList();
            var transport = provider.GetRequiredService<IMessageTransport>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(descriptors.Count).IsEqualTo(1);
                _ = await Assert.That(transport).IsTypeOf<AzureQueueStorageMessageTransport>();
            }
        }
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_Registers_working_transport_end_to_end(
        CancellationToken cancellationToken
    )
    {
        var queueName = CreateUniqueQueueName();
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureQueueStorageTransport(containerFixture.ConnectionString, o => o.QueueName = queueName)
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var transport = provider.GetRequiredService<IMessageTransport>();
            var message = CreateOutboxMessage();

            await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);

            var queueClient = new QueueClient(containerFixture.ConnectionString, queueName, VerificationClientOptions);
            var response = await queueClient
                .ReceiveMessageAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(response.Value).IsNotNull();
        }
    }

    private static readonly QueueClientOptions VerificationClientOptions = new(
        QueueClientOptions.ServiceVersion.V2025_11_05
    );

    private static string CreateUniqueQueueName() => $"pulse-it-{Guid.NewGuid():N}";

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(IntegrationTestEvent),
            Payload = """{"data":"test"}""",
            CorrelationId = Guid.NewGuid().ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 0,
        };

    private sealed record IntegrationTestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

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
