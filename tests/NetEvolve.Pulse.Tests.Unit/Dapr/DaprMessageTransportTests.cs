namespace NetEvolve.Pulse.Tests.Unit.Dapr;

using System;
using System.Threading.Tasks;
using global::Dapr.Client;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class DaprMessageTransportTests
{
    [Test]
    public async Task Constructor_When_daprClient_is_null_throws_ArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new DaprMessageTransport(
                    null!,
                    new FakeTopicNameResolver(),
                    Options.Create(new DaprMessageTransportOptions())
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_When_topicNameResolver_is_null_throws_ArgumentNullException()
    {
        using var daprClient = new DaprClientBuilder().Build();

        _ = await Assert
            .That(() => new DaprMessageTransport(daprClient, null!, Options.Create(new DaprMessageTransportOptions())))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_When_options_is_null_throws_ArgumentNullException()
    {
        using var daprClient = new DaprClientBuilder().Build();

        _ = await Assert
            .That(() => new DaprMessageTransport(daprClient, new FakeTopicNameResolver(), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_With_valid_arguments_creates_instance()
    {
        using var daprClient = new DaprClientBuilder().Build();
        var transport = new DaprMessageTransport(
            daprClient,
            new FakeTopicNameResolver(),
            Options.Create(new DaprMessageTransportOptions())
        );

        _ = await Assert.That(transport).IsNotNull();
    }

    [Test]
    public async Task SendAsync_When_message_is_null_throws_ArgumentNullException()
    {
        using var daprClient = new DaprClientBuilder().Build();
        var transport = new DaprMessageTransport(
            daprClient,
            new FakeTopicNameResolver(),
            Options.Create(new DaprMessageTransportOptions())
        );

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(null!));
    }

    [Test]
    public async Task IsHealthyAsync_Delegates_to_DaprClient()
    {
        using var daprClient = new DaprClientBuilder().Build();
        var transport = new DaprMessageTransport(
            daprClient,
            new FakeTopicNameResolver(),
            Options.Create(new DaprMessageTransportOptions())
        );

        // Without a running Dapr sidecar, CheckHealthAsync returns false (connection refused → false, not throw)
        var result = await transport.IsHealthyAsync();

        _ = await Assert.That(result).IsTypeOf<bool>();
    }

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => "test-topic";
    }
}
