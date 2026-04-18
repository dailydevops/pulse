namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Redis.Idempotency;
using TUnit.Core;

[TestGroup("Redis")]
public sealed class RedisIdempotencyKeyOptionsValidatorTests
{
    [Test]
    public async Task Validate_WithValidOptions_ReturnsSuccess()
    {
        var validator = new RedisIdempotencyKeyOptionsValidator();
        var options = new RedisIdempotencyKeyOptions
        {
            KeyPrefix = "pulse:idempotency:",
            TimeToLive = TimeSpan.FromHours(24),
        };

        var result = validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithZeroTimeToLive_ReturnsFail()
    {
        var validator = new RedisIdempotencyKeyOptionsValidator();
        var options = new RedisIdempotencyKeyOptions { TimeToLive = TimeSpan.Zero };

        var result = validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(nameof(RedisIdempotencyKeyOptions.TimeToLive));
        }
    }

    [Test]
    public async Task Validate_WithNegativeTimeToLive_ReturnsFail()
    {
        var validator = new RedisIdempotencyKeyOptionsValidator();
        var options = new RedisIdempotencyKeyOptions { TimeToLive = TimeSpan.FromSeconds(-1) };

        var result = validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(nameof(RedisIdempotencyKeyOptions.TimeToLive));
        }
    }

    [Test]
    public async Task Validate_WithNullKeyPrefix_ReturnsFail()
    {
        var validator = new RedisIdempotencyKeyOptionsValidator();
        var options = new RedisIdempotencyKeyOptions { KeyPrefix = null! };

        var result = validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(nameof(RedisIdempotencyKeyOptions.KeyPrefix));
        }
    }

    [Test]
    public async Task Validate_WithEmptyKeyPrefix_ReturnsFail()
    {
        var validator = new RedisIdempotencyKeyOptionsValidator();
        var options = new RedisIdempotencyKeyOptions { KeyPrefix = string.Empty };

        var result = validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(nameof(RedisIdempotencyKeyOptions.KeyPrefix));
        }
    }

    [Test]
    public async Task Validate_WithWhitespaceKeyPrefix_ReturnsFail()
    {
        var validator = new RedisIdempotencyKeyOptionsValidator();
        var options = new RedisIdempotencyKeyOptions { KeyPrefix = "   " };

        var result = validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(nameof(RedisIdempotencyKeyOptions.KeyPrefix));
        }
    }

    [Test]
    public async Task Validate_TimeToLiveCheckedBeforeKeyPrefix()
    {
        var validator = new RedisIdempotencyKeyOptionsValidator();
        var options = new RedisIdempotencyKeyOptions { TimeToLive = TimeSpan.Zero, KeyPrefix = string.Empty };

        var result = validator.Validate(null, options);

        // First failure encountered is TimeToLive
        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(nameof(RedisIdempotencyKeyOptions.TimeToLive));
        }
    }
}
