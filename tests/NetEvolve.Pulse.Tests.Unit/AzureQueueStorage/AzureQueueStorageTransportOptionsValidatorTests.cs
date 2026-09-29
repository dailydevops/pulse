namespace NetEvolve.Pulse.Tests.Unit.AzureQueueStorage;

using System.Threading;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("AzureQueueStorage")]
public sealed class AzureQueueStorageTransportOptionsValidatorTests
{
    private static readonly AzureQueueStorageTransportOptionsValidator _validator = new();

    [Test]
    public async Task Validate_When_neither_ConnectionString_nor_Uri_provided_fails()
    {
        var options = new AzureQueueStorageTransportOptions { ConnectionString = null, QueueServiceUri = null };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_When_ConnectionString_provided_succeeds()
    {
        var options = new AzureQueueStorageTransportOptions { ConnectionString = "UseDevelopmentStorage=true" };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_When_QueueServiceUri_provided_succeeds()
    {
        var options = new AzureQueueStorageTransportOptions
        {
            QueueServiceUri = new Uri("https://account.queue.core.windows.net"),
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_When_QueueName_is_empty_fails()
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            QueueName = string.Empty,
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_When_QueueName_is_whitespace_fails()
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            QueueName = "   ",
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_Default_options_without_ConnectionString_fails()
    {
        var options = new AzureQueueStorageTransportOptions();

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_ConnectionString_is_whitespace_and_no_Uri_fails()
    {
        var options = new AzureQueueStorageTransportOptions { ConnectionString = "   " };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_Both_ConnectionString_and_QueueServiceUri_provided_succeeds()
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            QueueServiceUri = new Uri("https://account.queue.core.windows.net"),
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(ValidVisibilityTimeouts))]
    public async Task Validate_When_MessageVisibilityTimeout_is_in_range_succeeds(TimeSpan visibilityTimeout)
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            MessageVisibilityTimeout = visibilityTimeout,
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(InvalidVisibilityTimeouts))]
    public async Task Validate_When_MessageVisibilityTimeout_is_out_of_range_fails(TimeSpan visibilityTimeout)
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            MessageVisibilityTimeout = visibilityTimeout,
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(ValidTimeToLives))]
    public async Task Validate_When_MessageTimeToLive_is_valid_succeeds(TimeSpan timeToLive)
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            MessageTimeToLive = timeToLive,
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(InvalidTimeToLives))]
    public async Task Validate_When_MessageTimeToLive_is_invalid_fails(TimeSpan timeToLive)
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            MessageTimeToLive = timeToLive,
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_When_MessageVisibilityTimeout_is_below_MessageTimeToLive_succeeds()
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            MessageVisibilityTimeout = TimeSpan.FromMinutes(5),
            MessageTimeToLive = TimeSpan.FromMinutes(10),
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(10)]
    [Arguments(15)]
    public async Task Validate_When_MessageVisibilityTimeout_is_not_below_MessageTimeToLive_fails(int minutes)
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            MessageVisibilityTimeout = TimeSpan.FromMinutes(minutes),
            MessageTimeToLive = TimeSpan.FromMinutes(10),
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_When_MessageVisibilityTimeout_and_MessageTimeToLive_truncate_to_same_seconds_fails()
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            MessageVisibilityTimeout = TimeSpan.FromMilliseconds(10_100),
            MessageTimeToLive = TimeSpan.FromMilliseconds(10_900),
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(NeverExpiresTimeToLives))]
    public async Task Validate_When_MessageTimeToLive_never_expires_accepts_maximum_MessageVisibilityTimeout(
        TimeSpan timeToLive
    )
    {
        var options = new AzureQueueStorageTransportOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            MessageVisibilityTimeout = TimeSpan.FromDays(7),
            MessageTimeToLive = timeToLive,
        };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    public static IEnumerable<Func<TimeSpan>> ValidVisibilityTimeouts()
    {
        yield return () => TimeSpan.Zero;
        yield return () => TimeSpan.FromSeconds(30);
        yield return () => TimeSpan.FromDays(7);
    }

    public static IEnumerable<Func<TimeSpan>> InvalidVisibilityTimeouts()
    {
        yield return () => TimeSpan.FromSeconds(-1);
        yield return () => Timeout.InfiniteTimeSpan;
        yield return () => TimeSpan.FromDays(7).Add(TimeSpan.FromSeconds(1));
        yield return () => TimeSpan.FromDays(8);
    }

    public static IEnumerable<Func<TimeSpan>> ValidTimeToLives()
    {
        yield return () => TimeSpan.FromSeconds(1);
        yield return () => TimeSpan.FromDays(7);
        yield return () => TimeSpan.FromDays(30);
        yield return () => TimeSpan.FromSeconds(-1);
        yield return () => Timeout.InfiniteTimeSpan;
    }

    public static IEnumerable<Func<TimeSpan>> InvalidTimeToLives()
    {
        yield return () => TimeSpan.Zero;
        yield return () => TimeSpan.FromMilliseconds(500);
        yield return () => TimeSpan.FromSeconds(-2);
        yield return () => TimeSpan.FromDays(-1);
    }

    public static IEnumerable<Func<TimeSpan>> NeverExpiresTimeToLives()
    {
        yield return () => TimeSpan.FromSeconds(-1);
        yield return () => Timeout.InfiniteTimeSpan;
    }
}
