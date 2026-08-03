namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("Outbox")]
public class OutboxProcessorOptionsValidatorTests
{
    private static readonly OutboxProcessorOptionsValidator _validator = new();

    [Test]
    public async Task Validate_DefaultOptions_Succeeds()
    {
        var options = new OutboxProcessorOptions();

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithZeroBatchSize_Fails()
    {
        var options = new OutboxProcessorOptions { BatchSize = 0 };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithNegativeBatchSize_Fails()
    {
        var options = new OutboxProcessorOptions { BatchSize = -1 };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithZeroPollingInterval_Fails()
    {
        var options = new OutboxProcessorOptions { PollingInterval = TimeSpan.Zero };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithNegativePollingInterval_Fails()
    {
        var options = new OutboxProcessorOptions { PollingInterval = TimeSpan.FromSeconds(-1) };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithNegativeMaxRetryCount_Fails()
    {
        var options = new OutboxProcessorOptions { MaxRetryCount = -1 };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithZeroMaxRetryCount_Succeeds()
    {
        var options = new OutboxProcessorOptions { MaxRetryCount = 0 };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithZeroProcessingTimeout_Fails()
    {
        var options = new OutboxProcessorOptions { ProcessingTimeout = TimeSpan.Zero };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithExponentialBackoffEnabled_AndInvalidBackoffMultiplier_Fails()
    {
        var options = new OutboxProcessorOptions { EnableExponentialBackoff = true, BackoffMultiplier = 1.0 };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithExponentialBackoffEnabled_AndZeroBaseRetryDelay_Fails()
    {
        var options = new OutboxProcessorOptions { EnableExponentialBackoff = true, BaseRetryDelay = TimeSpan.Zero };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithExponentialBackoffEnabled_AndMaxRetryDelayLessThanBaseRetryDelay_Fails()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromSeconds(5),
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithExponentialBackoffEnabled_AndValidValues_Succeeds()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BackoffMultiplier = 2.0,
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            MaxRetryDelay = TimeSpan.FromMinutes(5),
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithExponentialBackoffDisabled_IgnoresBackoffFields()
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = false,
            BackoffMultiplier = 0,
            BaseRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }
}
