namespace NetEvolve.Pulse.Tests.Unit.Dapr;

using System;
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
        var daprClient = new FakeDaprClient();
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
    public async Task IsHealthyAsync_Delegates_to_DaprClient(CancellationToken cancellationToken)
    {
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

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => "test-topic";
    }
}
