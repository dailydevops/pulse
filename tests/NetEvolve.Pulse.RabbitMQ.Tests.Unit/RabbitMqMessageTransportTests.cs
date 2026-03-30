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
    public async Task Options_ExchangeName_can_be_configured()
    {
        var options = new RabbitMqTransportOptions { ExchangeName = "test-exchange" };

        _ = await Assert.That(options.ExchangeName).IsEqualTo("test-exchange");
    }

    [Test]
    public async Task Options_Default_ExchangeName_is_empty_string()
    {
        var options = new RabbitMqTransportOptions();

        _ = await Assert.That(options.ExchangeName).IsEqualTo(string.Empty);
    }
}
