namespace NetEvolve.Pulse.RabbitMQ.Tests.Unit;

using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Unit tests for RabbitMqMessageTransport.
/// Note: Due to the complexity of RabbitMQ.Client interfaces, most behavioral tests
/// are in RabbitMqTransportIntegrationTests using Testcontainers.
/// These unit tests focus on constructor validation and options configuration.
/// </summary>
public sealed class RabbitMqMessageTransportTests
{
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

    [Test]
    public async Task Options_Default_values_are_correct()
    {
        var options = new RabbitMqTransportOptions();

        using (Assert.Multiple())
        {
            _ = await Assert.That(options.HostName).IsEqualTo("localhost");
            _ = await Assert.That(options.Port).IsEqualTo(5672);
            _ = await Assert.That(options.VirtualHost).IsEqualTo("/");
            _ = await Assert.That(options.UserName).IsEqualTo("guest");
            _ = await Assert.That(options.Password).IsEqualTo("guest");
            _ = await Assert.That(options.ExchangeName).IsEqualTo("outbox");
        }
    }
}
