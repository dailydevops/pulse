namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System;
using System.Linq;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("Redis")]
public class RedisStreamsTransportOptionsValidatorTests
{
    private static readonly RedisStreamsTransportOptionsValidator _validator = new();

    [Test]
    public async Task Validate_DefaultOptions_Succeeds()
    {
        var options = new RedisStreamsTransportOptions();

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithEmptyStreamKey_Fails()
    {
        var options = new RedisStreamsTransportOptions { StreamKey = string.Empty };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            var failures = result.Failures!.ToArray();
            _ = await Assert
                .That(
                    failures.Any(f =>
                        f.Contains(nameof(RedisStreamsTransportOptions.StreamKey), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Validate_WithNullStreamKey_Fails()
    {
        var options = new RedisStreamsTransportOptions { StreamKey = null! };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            var failures = result.Failures!.ToArray();
            _ = await Assert
                .That(
                    failures.Any(f =>
                        f.Contains(nameof(RedisStreamsTransportOptions.StreamKey), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Validate_WithWhitespaceStreamKey_Fails()
    {
        var options = new RedisStreamsTransportOptions { StreamKey = "   " };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            var failures = result.Failures!.ToArray();
            _ = await Assert
                .That(
                    failures.Any(f =>
                        f.Contains(nameof(RedisStreamsTransportOptions.StreamKey), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Validate_WithEmptyConsumerGroupName_Fails()
    {
        var options = new RedisStreamsTransportOptions { ConsumerGroupName = string.Empty };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            var failures = result.Failures!.ToArray();
            _ = await Assert
                .That(
                    failures.Any(f =>
                        f.Contains(nameof(RedisStreamsTransportOptions.ConsumerGroupName), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Validate_WithNullConsumerGroupName_Fails()
    {
        var options = new RedisStreamsTransportOptions { ConsumerGroupName = null! };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            var failures = result.Failures!.ToArray();
            _ = await Assert
                .That(
                    failures.Any(f =>
                        f.Contains(nameof(RedisStreamsTransportOptions.ConsumerGroupName), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Validate_WithWhitespaceConsumerGroupName_Fails()
    {
        var options = new RedisStreamsTransportOptions { ConsumerGroupName = "   " };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            var failures = result.Failures!.ToArray();
            _ = await Assert
                .That(
                    failures.Any(f =>
                        f.Contains(nameof(RedisStreamsTransportOptions.ConsumerGroupName), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Validate_WithBothStreamKeyAndConsumerGroupNameInvalid_FailsWithBothMessages()
    {
        var options = new RedisStreamsTransportOptions { StreamKey = string.Empty, ConsumerGroupName = string.Empty };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            var failures = result.Failures!.ToArray();
            _ = await Assert.That(failures.Length).IsEqualTo(2);
            _ = await Assert
                .That(
                    failures.Any(f =>
                        f.Contains(nameof(RedisStreamsTransportOptions.StreamKey), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
            _ = await Assert
                .That(
                    failures.Any(f =>
                        f.Contains(nameof(RedisStreamsTransportOptions.ConsumerGroupName), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }
}
