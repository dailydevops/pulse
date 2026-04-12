namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Unit tests for exponential backoff calculation in <see cref="OutboxProcessorOptions"/>.
/// Tests backoff formula, jitter application, and delay clamping.
/// </summary>
[TestGroup("Outbox")]
public sealed class ExponentialBackoffTests
{
    [Test]
    [Arguments(0, 5)]
    [Arguments(1, 10)]
    [Arguments(2, 20)]
    public async Task ComputeNextRetryAt_WithNoJitter_ComputesCorrectBackoff(
        int retryCount,
        int expectedSeconds,
        CancellationToken cancellationToken
    )
    {
        var options = new OutboxProcessorOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            AddJitter = false,
            MaxRetryDelay = TimeSpan.FromMinutes(5),
        };

        var now = DateTimeOffset.UtcNow;
        var nextRetryAt = options.ComputeNextRetryAt(now, retryCount);

        var expectedDelay = TimeSpan.FromSeconds(expectedSeconds);
        var expectedTime = now.Add(expectedDelay);

        // Allow 100ms tolerance for test execution time
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsLessThanOrEqualTo(100);
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsGreaterThanOrEqualTo(-100);
    }

    [Test]
    public async Task ComputeNextRetryAt_WithMaxRetryDelayExceeded_ClampsToMax(CancellationToken cancellationToken)
    {
        var options = new OutboxProcessorOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            AddJitter = false,
            MaxRetryDelay = TimeSpan.FromSeconds(30),
        };

        var now = DateTimeOffset.UtcNow;
        // Retry count 3: 5 * 2^3 = 40 seconds, should be clamped to 30
        var nextRetryAt = options.ComputeNextRetryAt(now, 3);

        var expectedTime = now.Add(TimeSpan.FromSeconds(30));

        // Allow 100ms tolerance
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsLessThanOrEqualTo(100);
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsGreaterThanOrEqualTo(-100);
    }

    [Test]
    public async Task ComputeNextRetryAt_WithJitter_AddsRandomizedDelay(CancellationToken cancellationToken)
    {
        var options = new OutboxProcessorOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 1.0, // No exponential growth for simplicity
            AddJitter = true,
            MaxRetryDelay = TimeSpan.FromMinutes(5),
        };

        var now = DateTimeOffset.UtcNow;
        var nextRetryAt = options.ComputeNextRetryAt(now, 0);

        // Base delay is 10 seconds, jitter is up to 20% of 10 = 2 seconds
        // So the total should be between 10 and 12 seconds
        var minExpectedTime = now.Add(TimeSpan.FromSeconds(10));
        var maxExpectedTime = now.Add(TimeSpan.FromSeconds(12));

        _ = await Assert.That(nextRetryAt).IsGreaterThanOrEqualTo(minExpectedTime);
        _ = await Assert.That(nextRetryAt).IsLessThanOrEqualTo(maxExpectedTime);
    }

    [Test]
    public async Task ComputeNextRetryAt_WithMultipleSamples_JitterIsRandomized(CancellationToken cancellationToken)
    {
        var options = new OutboxProcessorOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 1.0,
            AddJitter = true,
            MaxRetryDelay = TimeSpan.FromMinutes(5),
        };

        var now = DateTimeOffset.UtcNow;
        var times = new List<DateTimeOffset>();

        for (var i = 0; i < 10; i++)
        {
            var nextRetryAt = options.ComputeNextRetryAt(now, 0);
            times.Add(nextRetryAt);
        }

        // Verify that not all times are the same (randomization is working)
        var distinctTimes = times.Distinct().Count();
        _ = await Assert.That(distinctTimes).IsGreaterThan(1);
    }

    [Test]
    public async Task ComputeNextRetryAt_WithZeroRetryCount_UsesBaseDelay(CancellationToken cancellationToken)
    {
        var options = new OutboxProcessorOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            AddJitter = false,
            MaxRetryDelay = TimeSpan.FromMinutes(5),
        };

        var now = DateTimeOffset.UtcNow;
        var nextRetryAt = options.ComputeNextRetryAt(now, 0);

        var expectedTime = now.Add(TimeSpan.FromSeconds(5));

        // Allow 100ms tolerance
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsLessThanOrEqualTo(100);
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsGreaterThanOrEqualTo(-100);
    }

    [Test]
    public async Task OutboxProcessorOptions_HasCorrectDefaults(CancellationToken cancellationToken)
    {
        var options = new OutboxProcessorOptions();

        _ = await Assert.That(options.EnableExponentialBackoff).IsFalse();
        _ = await Assert.That(options.BaseRetryDelay).IsEqualTo(TimeSpan.FromSeconds(5));
        _ = await Assert.That(options.MaxRetryDelay).IsEqualTo(TimeSpan.FromMinutes(5));
        _ = await Assert.That(options.BackoffMultiplier).IsEqualTo(2.0);
        _ = await Assert.That(options.AddJitter).IsTrue();
    }

    [Test]
    public async Task OutboxProcessorOptions_CanConfigureCustomValues(CancellationToken cancellationToken)
    {
        var options = new OutboxProcessorOptions
        {
            EnableExponentialBackoff = true,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromMinutes(2),
            BackoffMultiplier = 1.5,
            AddJitter = false,
        };

        _ = await Assert.That(options.EnableExponentialBackoff).IsTrue();
        _ = await Assert.That(options.BaseRetryDelay).IsEqualTo(TimeSpan.FromSeconds(10));
        _ = await Assert.That(options.MaxRetryDelay).IsEqualTo(TimeSpan.FromMinutes(2));
        _ = await Assert.That(options.BackoffMultiplier).IsEqualTo(1.5);
        _ = await Assert.That(options.AddJitter).IsFalse();
    }

    [Test]
    public async Task ComputeNextRetryAt_WithLargeRetryCount_DoesNotOverflow(CancellationToken cancellationToken)
    {
        var options = new OutboxProcessorOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            AddJitter = false,
            MaxRetryDelay = TimeSpan.FromMinutes(5),
        };

        var now = DateTimeOffset.UtcNow;
        // This would cause overflow without protection
        var nextRetryAt = options.ComputeNextRetryAt(now, 100);

        // Should be clamped to MaxRetryDelay
        var expectedTime = now.Add(TimeSpan.FromMinutes(5));

        // Allow 100ms tolerance
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsLessThanOrEqualTo(100);
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsGreaterThanOrEqualTo(-100);
    }

    [Test]
    public async Task ComputeNextRetryAt_WithBaseDelayGreaterThanMax_ClampsCorrectly(
        CancellationToken cancellationToken
    )
    {
        var options = new OutboxProcessorOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(60),
            BackoffMultiplier = 1.0, // No exponential growth
            AddJitter = false,
            MaxRetryDelay = TimeSpan.FromSeconds(30),
        };

        var now = DateTimeOffset.UtcNow;
        var nextRetryAt = options.ComputeNextRetryAt(now, 0);

        // Base delay (60s) exceeds max (30s), should be clamped
        var expectedTime = now.Add(TimeSpan.FromSeconds(30));

        // Allow 100ms tolerance
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsLessThanOrEqualTo(100);
        _ = await Assert.That((nextRetryAt - expectedTime).TotalMilliseconds).IsGreaterThanOrEqualTo(-100);
    }
}
