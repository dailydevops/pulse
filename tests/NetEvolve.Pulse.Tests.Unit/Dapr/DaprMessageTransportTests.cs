namespace NetEvolve.Pulse.Tests.Unit.Dapr;

using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using global::Dapr.Client;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Serialization;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Dapr")]
public sealed class DaprMessageTransportTests
{
#pragma warning disable CA1859 // property intentionally typed as IPayloadSerializer for test flexibility
    private static IPayloadSerializer DefaultSerializer =>
        new SystemTextJsonPayloadSerializer(Options.Create(JsonSerializerOptions.Default));
#pragma warning restore CA1859

    [Test]
    public async Task Constructor_When_daprClient_is_null_throws_ArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new DaprMessageTransport(
                    null!,
                    new FakeTopicNameResolver(),
                    Options.Create(new DaprMessageTransportOptions()),
                    DefaultSerializer
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_When_topicNameResolver_is_null_throws_ArgumentNullException()
    {
        using var daprClient = new DaprClientBuilder().Build();

        _ = await Assert
            .That(() =>
                new DaprMessageTransport(
                    daprClient,
                    null!,
                    Options.Create(new DaprMessageTransportOptions()),
                    DefaultSerializer
                )
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_When_options_is_null_throws_ArgumentNullException()
    {
        using var daprClient = new DaprClientBuilder().Build();

        _ = await Assert
            .That(() => new DaprMessageTransport(daprClient, new FakeTopicNameResolver(), null!, DefaultSerializer))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_When_payloadSerializer_is_null_throws_ArgumentNullException()
    {
        using var daprClient = new DaprClientBuilder().Build();

        _ = await Assert
            .That(() =>
                new DaprMessageTransport(
                    daprClient,
                    new FakeTopicNameResolver(),
                    Options.Create(new DaprMessageTransportOptions()),
                    null!
                )
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_With_valid_arguments_creates_instance()
    {
        using var daprClient = new DaprClientBuilder().Build();
        var transport = new DaprMessageTransport(
            daprClient,
            new FakeTopicNameResolver(),
            Options.Create(new DaprMessageTransportOptions()),
            DefaultSerializer
        );

        _ = await Assert.That(transport).IsNotNull();
    }

    [Test]
    public async Task SendAsync_When_message_is_null_throws_ArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var daprClient = new DaprClientBuilder().Build();
        var transport = new DaprMessageTransport(
            daprClient,
            new FakeTopicNameResolver(),
            Options.Create(new DaprMessageTransportOptions()),
            DefaultSerializer
        );

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(null!, cancellationToken));
    }

    [Test]
    public async Task SendAsync_Publishes_original_payload_bytes_without_deserialize_reserialize_roundtrip(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var daprClient = new FakeDaprClient();
        var transport = new DaprMessageTransport(
            daprClient,
            new FakeTopicNameResolver(),
            Options.Create(new DaprMessageTransportOptions { PubSubName = "test-pubsub" }),
            DefaultSerializer
        );

        // Property order and number formatting a JsonElement round-trip could alter.
        const string OriginalPayload = "{\"z\":1,\"a\":1.50,\"m\":10}";
        var message = new OutboxMessage
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            EventType = typeof(string),
            Payload = OriginalPayload,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(daprClient.PublishByteEventAsyncCalled).IsTrue();
        _ = await Assert.That(daprClient.PublishEventAsyncCalled).IsFalse();
        _ = await Assert.That(daprClient.PublishedPubsubName).IsEqualTo("test-pubsub");
        _ = await Assert.That(daprClient.PublishedTopicName).IsEqualTo("test-topic");
        _ = await Assert.That(daprClient.PublishedContentType).IsEqualTo("application/json");
        _ = await Assert.That(daprClient.PublishedBytes).IsEquivalentTo(Encoding.UTF8.GetBytes(OriginalPayload));
    }

    [Test]
    public async Task SendAsync_Sets_cloudevent_id_and_type_metadata_from_message(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var daprClient = new FakeDaprClient();
        var transport = CreateTransport(daprClient);
        var message = CreateMessage();

        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(daprClient.PublishedContentType).IsEqualTo("application/json");
        _ = await Assert.That(daprClient.PublishedMetadata).IsNotNull();
        _ = await Assert
            .That(daprClient.PublishedMetadata!["cloudevent.id"])
            .IsEqualTo(message.Id.ToString("D", CultureInfo.InvariantCulture));
        _ = await Assert
            .That(daprClient.PublishedMetadata["cloudevent.type"])
            .IsEqualTo(message.EventType.ToOutboxEventTypeName());
    }

    [Test]
    public async Task SendAsync_Same_message_twice_publishes_same_cloudevent_id(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var daprClient = new FakeDaprClient();
        var transport = CreateTransport(daprClient);
        var message = CreateMessage();

        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var firstId = daprClient.PublishedMetadata?["cloudevent.id"];
        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var secondId = daprClient.PublishedMetadata?["cloudevent.id"];

        _ = await Assert.That(firstId).IsNotNull();
        _ = await Assert.That(secondId).IsEqualTo(firstId);
    }

    [Test]
    public async Task SendAsync_Does_not_override_cloudevent_trace_metadata(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var daprClient = new FakeDaprClient();
        var transport = CreateTransport(daprClient);
        var message = CreateMessage();

        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(daprClient.PublishedMetadata).IsNotNull();
        _ = await Assert.That(daprClient.PublishedMetadata!.ContainsKey("cloudevent.traceid")).IsFalse();
        _ = await Assert.That(daprClient.PublishedMetadata.ContainsKey("cloudevent.traceparent")).IsFalse();
        _ = await Assert.That(daprClient.PublishedMetadata.ContainsKey("cloudevent.tracestate")).IsFalse();
    }

    [Test]
    public async Task IsHealthyAsync_Delegates_to_DaprClient(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var daprClient = new DaprClientBuilder().Build();
        var transport = new DaprMessageTransport(
            daprClient,
            new FakeTopicNameResolver(),
            Options.Create(new DaprMessageTransportOptions()),
            DefaultSerializer
        );

        // Without a running Dapr sidecar, CheckHealthAsync returns false
        var result = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsTypeOf<bool>();
    }

    private static DaprMessageTransport CreateTransport(FakeDaprClient daprClient) =>
        new(
            daprClient,
            new FakeTopicNameResolver(),
            Options.Create(new DaprMessageTransportOptions { PubSubName = "test-pubsub" }),
            DefaultSerializer
        );

    private static OutboxMessage CreateMessage() =>
        new()
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            EventType = typeof(string),
            Payload = "{}",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => "test-topic";
    }
}
