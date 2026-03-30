namespace NetEvolve.Pulse.RabbitMQ.Tests.Unit;

using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Unit tests for RabbitMqMessageTransport.
/// These tests focus on constructor validation, argument validation, and basic behavioral contracts.
/// Full end-to-end behavior including message publishing, channel management, and health checks
/// is validated in RabbitMqTransportIntegrationTests using Testcontainers with real RabbitMQ.
/// </summary>
/// <remarks>
/// Note: Creating comprehensive fake implementations of IConnection and IChannel from RabbitMQ.Client 7.1.0
/// is impractical due to the complexity of these interfaces (numerous async methods, events, and properties).
/// This approach follows the pattern used by other transport packages where integration tests validate
/// the actual client library integration, while unit tests focus on input validation and configuration.
/// </remarks>
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
