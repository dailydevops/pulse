namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("RabbitMQ")]
public class RabbitMqTransportOptionsValidatorTests
{
    private static readonly RabbitMqTransportOptionsValidator _validator = new();

    [Test]
    public async Task Validate_WithValidExchangeNameAndDefaultPoolSize_Succeeds()
    {
        var options = new RabbitMqTransportOptions { ExchangeName = "my-exchange" };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithEmptyExchangeName_Fails()
    {
        var options = new RabbitMqTransportOptions { ExchangeName = string.Empty };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithWhitespaceExchangeName_Fails()
    {
        var options = new RabbitMqTransportOptions { ExchangeName = "   " };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithZeroMaxChannelPoolSize_Fails()
    {
        var options = new RabbitMqTransportOptions { ExchangeName = "my-exchange", MaxChannelPoolSize = 0 };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithNegativeMaxChannelPoolSize_Fails()
    {
        var options = new RabbitMqTransportOptions { ExchangeName = "my-exchange", MaxChannelPoolSize = -1 };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithMinimumValidMaxChannelPoolSize_Succeeds()
    {
        var options = new RabbitMqTransportOptions { ExchangeName = "my-exchange", MaxChannelPoolSize = 1 };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithBothExchangeNameAndPoolSizeInvalid_FailsWithBothMessages()
    {
        var options = new RabbitMqTransportOptions { ExchangeName = string.Empty, MaxChannelPoolSize = 0 };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.Failures!.Count()).IsEqualTo(2);
        }
    }
}
