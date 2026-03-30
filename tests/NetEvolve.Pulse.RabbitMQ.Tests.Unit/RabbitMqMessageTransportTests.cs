namespace NetEvolve.Pulse.RabbitMQ.Tests.Unit;

using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class RabbitMqMessageTransportTests
{
    [Test]
    public async Task Constructor_When_topicNameResolver_null_throws()
    {
        ITopicNameResolver topicNameResolver = null!;
        var options = Options.Create(new RabbitMqTransportOptions());

#pragma warning disable CA1806 // Do not ignore method results
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqMessageTransport(topicNameResolver, options)
        );
#pragma warning restore CA1806

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("topicNameResolver");
    }

    [Test]
    public async Task Constructor_When_options_null_throws()
    {
        var topicNameResolver = new FakeTopicNameResolver();
        IOptions<RabbitMqTransportOptions> options = null!;

#pragma warning disable CA1806 // Do not ignore method results
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqMessageTransport(topicNameResolver, options)
        );
#pragma warning restore CA1806

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("options");
    }

    [Test]
    public async Task SendAsync_When_message_null_throws()
    {
        using var transport = CreateTransport();
        OutboxMessage message = null!;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(message));

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("message");
    }

    [Test]
    public async Task IsHealthyAsync_When_not_connected_returns_false()
    {
        using var transport = CreateTransport();

        var healthy = await transport.IsHealthyAsync();

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task Options_Are_properly_configured()
    {
        var options = new RabbitMqTransportOptions
        {
            HostName = "test-host",
            Port = 5673,
            VirtualHost = "/test",
            UserName = "test-user",
            Password = "test-pass",
            ExchangeName = "test-exchange",
            RoutingKey = "test.routing.key",
        };

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.HostName).IsEqualTo("test-host");
            _ = await Assert.That(options.Port).IsEqualTo(5673);
            _ = await Assert.That(options.VirtualHost).IsEqualTo("/test");
            _ = await Assert.That(options.UserName).IsEqualTo("test-user");
            _ = await Assert.That(options.Password).IsEqualTo("test-pass");
            _ = await Assert.That(options.ExchangeName).IsEqualTo("test-exchange");
            _ = await Assert.That(options.RoutingKey).IsEqualTo("test.routing.key");
        }
    }

    private static RabbitMqMessageTransport CreateTransport(RabbitMqTransportOptions? options = null)
    {
        options ??= new RabbitMqTransportOptions
        {
            HostName = "localhost",
            Port = 5672,
            ExchangeName = "test-exchange",
        };

        return new RabbitMqMessageTransport(new FakeTopicNameResolver(), Options.Create(options));
    }

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => "FakeTopic";
    }
}
