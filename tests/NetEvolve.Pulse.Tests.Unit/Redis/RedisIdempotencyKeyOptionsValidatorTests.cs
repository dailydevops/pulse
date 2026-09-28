namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System;
using System.Linq;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

[TestGroup("Redis")]
public class RedisIdempotencyKeyOptionsValidatorTests
{
    private static readonly RedisIdempotencyKeyOptionsValidator _validator = new();

    [Test]
    public async Task Validate_DefaultOptions_Succeeds()
    {
        var result = _validator.Validate(null, new IdempotencyKeyOptions());

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithNullTimeToLive_Succeeds()
    {
        var result = _validator.Validate(null, new IdempotencyKeyOptions { TimeToLive = null });

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithPositiveTimeToLive_Succeeds()
    {
        var result = _validator.Validate(null, new IdempotencyKeyOptions { TimeToLive = TimeSpan.FromMinutes(5) });

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Validate_WithNullOrEmptySchema_Succeeds(string? schema)
    {
        var result = _validator.Validate(null, new IdempotencyKeyOptions { Schema = schema });

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Validate_WithNonPositiveTimeToLive_Fails(int seconds)
    {
        var result = _validator.Validate(
            null,
            new IdempotencyKeyOptions { TimeToLive = TimeSpan.FromSeconds(seconds) }
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert
                .That(
                    result.Failures!.Any(f =>
                        f.Contains(nameof(IdempotencyKeyOptions.TimeToLive), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Validate_WithTimeToLiveThatOverflowsPhysicalExpiry_Fails()
    {
        var result = _validator.Validate(null, new IdempotencyKeyOptions { TimeToLive = TimeSpan.MaxValue });

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert
                .That(
                    result.Failures!.Any(f =>
                        f.Contains(nameof(IdempotencyKeyOptions.TimeToLive), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Validate_WithInvalidTableName_Fails(string? tableName)
    {
        var result = _validator.Validate(null, new IdempotencyKeyOptions { TableName = tableName! });

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert
                .That(
                    result.Failures!.Any(f =>
                        f.Contains(nameof(IdempotencyKeyOptions.TableName), StringComparison.Ordinal)
                    )
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Validate_WithInvalidTableNameAndTimeToLive_FailsWithBothMessages()
    {
        var options = new IdempotencyKeyOptions { TableName = string.Empty, TimeToLive = TimeSpan.Zero };

        var result = _validator.Validate(null, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.Failures!.Count()).IsEqualTo(2);
        }
    }
}
