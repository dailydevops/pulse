namespace NetEvolve.Pulse.RabbitMQ.Tests.Unit;

using global::RabbitMQ.Client;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public sealed class RabbitMqMessageTransportTests
{
    [Test]
    public async Task Constructor_When_connection_null_throws()
    {
        IConnection connection = null!;
        var topicNameResolver = new FakeTopicNameResolver();
        var options = Options.Create(new RabbitMqTransportOptions());

#pragma warning disable CA1806 // Do not ignore method results
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqMessageTransport(connection, topicNameResolver, options)
        );
#pragma warning restore CA1806

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.ParamName).IsEqualTo("connection");
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
        };

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.HostName).IsEqualTo("test-host");
            _ = await Assert.That(options.Port).IsEqualTo(5673);
            _ = await Assert.That(options.VirtualHost).IsEqualTo("/test");
            _ = await Assert.That(options.UserName).IsEqualTo("test-user");
            _ = await Assert.That(options.Password).IsEqualTo("test-pass");
            _ = await Assert.That(options.ExchangeName).IsEqualTo("test-exchange");
        }
    }

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => "FakeTopic";
    }
}
