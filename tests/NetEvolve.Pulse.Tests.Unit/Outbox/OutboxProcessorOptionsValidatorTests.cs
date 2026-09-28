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
    public async Task Validate_WithZeroMaxRetryCount_Fails()
    {
        var options = new OutboxProcessorOptions { MaxRetryCount = 0 };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains("must be greater than or equal to 1");
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task Validate_WithPositiveMaxRetryCount_Succeeds(int maxRetryCount)
    {
        var options = new OutboxProcessorOptions { MaxRetryCount = maxRetryCount };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Validate_WithOverrideMaxRetryCountBelowOne_Fails(int maxRetryCount)
    {
        var options = new OutboxProcessorOptions
        {
            EventTypeOverrides =
            {
                [typeof(OverrideEvent)] = new OutboxEventTypeOptions { MaxRetryCount = maxRetryCount },
            },
        };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(typeof(OverrideEvent).FullName!);
        }
    }

    [Test]
    public async Task Validate_WithValidOverrideMaxRetryCount_Succeeds()
    {
        var options = new OutboxProcessorOptions
        {
            EventTypeOverrides =
            {
                [typeof(OverrideEvent)] = new OutboxEventTypeOptions { MaxRetryCount = 1 },
                [typeof(OutboxProcessorOptionsValidatorTests)] = new OutboxEventTypeOptions(),
            },
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(-2)]
    public async Task Validate_WithOverrideProcessingTimeoutNotPositive_Fails(int milliseconds)
    {
        var options = new OutboxProcessorOptions
        {
            EventTypeOverrides =
            {
                [typeof(OverrideEvent)] = new OutboxEventTypeOptions
                {
                    ProcessingTimeout = TimeSpan.FromMilliseconds(milliseconds),
                },
            },
        };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(typeof(OverrideEvent).FullName!);
            _ = await Assert.That(result.FailureMessage).Contains(nameof(OutboxEventTypeOptions.ProcessingTimeout));
        }
    }

    [Test]
    public async Task Validate_WithOverrideProcessingTimeoutAboveCancelAfterLimit_Fails()
    {
        var options = new OutboxProcessorOptions
        {
            EventTypeOverrides =
            {
                [typeof(OverrideEvent)] = new OutboxEventTypeOptions
                {
                    ProcessingTimeout = TimeSpan.FromMilliseconds(int.MaxValue + 1d),
                },
            },
        };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(typeof(OverrideEvent).FullName!);
        }
    }

    [Test]
    public async Task Validate_WithValidOverrideProcessingTimeout_Succeeds()
    {
        var options = new OutboxProcessorOptions
        {
            EventTypeOverrides =
            {
                [typeof(OverrideEvent)] = new OutboxEventTypeOptions
                {
                    ProcessingTimeout = TimeSpan.FromMilliseconds(int.MaxValue),
                },
                [typeof(OutboxProcessorOptionsValidatorTests)] = new OutboxEventTypeOptions
                {
                    MaxRetryCount = null,
                    ProcessingTimeout = null,
                    EnableBatchSending = null,
                },
            },
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithOverrideAndGlobalFailures_ReportsAllFailures()
    {
        var options = new OutboxProcessorOptions
        {
            BatchSize = 0,
            EventTypeOverrides =
            {
                [typeof(OverrideEvent)] = new OutboxEventTypeOptions
                {
                    MaxRetryCount = 0,
                    ProcessingTimeout = TimeSpan.Zero,
                },
            },
        };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.Failures!.Count()).IsEqualTo(3);
        }
    }

    [Test]
    public async Task Validate_WithNullOverrideValue_FailsWithoutThrowing()
    {
        var options = new OutboxProcessorOptions { EventTypeOverrides = { [typeof(OverrideEvent)] = null! } };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(typeof(OverrideEvent).FullName!);
        }
    }

    [Test]
    public async Task Validate_WithProcessingTimeoutAboveCancelAfterLimit_Fails()
    {
        var options = new OutboxProcessorOptions { ProcessingTimeout = TimeSpan.FromMilliseconds(int.MaxValue + 1d) };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithProcessingTimeoutAtCancelAfterLimit_Succeeds()
    {
        var options = new OutboxProcessorOptions { ProcessingTimeout = TimeSpan.FromMilliseconds(int.MaxValue) };

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

    private sealed class OverrideEvent;
}
