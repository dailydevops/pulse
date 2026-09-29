namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("CosmosDb")]
public sealed class CosmosDbOutboxOptionsValidatorTests
{
    private static readonly CosmosDbOutboxOptionsValidator _validator = new();

    [Test]
    public async Task Validate_When_PartitionKeyPath_is_default_succeeds()
    {
        var result = _validator.Validate(null, new CosmosDbOutboxOptions());

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("/eventType")]
    [Arguments("/Id")]
    [Arguments("/id/")]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Validate_When_PartitionKeyPath_is_unsupported_fails(string partitionKeyPath)
    {
        var result = _validator.Validate(null, new CosmosDbOutboxOptions { PartitionKeyPath = partitionKeyPath });

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(nameof(CosmosDbOutboxOptions.PartitionKeyPath));
            _ = await Assert.That(result.FailureMessage).Contains("/id");
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Validate_When_ProcessingLeaseTimeout_is_not_positive_fails(int leaseSeconds)
    {
        var result = _validator.Validate(
            null,
            new CosmosDbOutboxOptions { ProcessingLeaseTimeout = TimeSpan.FromSeconds(leaseSeconds) }
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Failed).IsTrue();
            _ = await Assert.That(result.FailureMessage).Contains(nameof(CosmosDbOutboxOptions.ProcessingLeaseTimeout));
        }
    }
}
