namespace NetEvolve.Pulse.Tests.Unit.AzureQueueStorage;

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
}
