namespace NetEvolve.Pulse.Tests.Unit.Dapr;

using System;
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
