namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Phase 2 audit U06: Asserts the *desirable* defaults for <see cref="OutboxProcessorOptions"/>.
/// Currently FAILS — defaults are configured as a foot-gun:
/// <c>MaxRetryCount=3</c>, <c>EnableExponentialBackoff=false</c>, <c>EnableBatchSending=false</c>.
/// Phase 3 should either flip these to the safer values asserted below or, if the current
/// values are intentional, repurpose this test (and update its assertions/message) as the
/// documented-choice contract test.
/// </summary>
[TestGroup("Outbox")]
public sealed class OutboxProcessorOptionsDefaultsTests
{
    [Test]
    public async Task Default_MaxRetryCount_should_be_at_least_5_to_survive_transient_failures()
    {
        var options = new OutboxProcessorOptions();

        _ = await Assert.That(options.MaxRetryCount).IsGreaterThanOrEqualTo(5);
    }

    [Test]
    public async Task Default_EnableExponentialBackoff_should_be_true_to_avoid_thundering_herd()
    {
        var options = new OutboxProcessorOptions();

        _ = await Assert.That(options.EnableExponentialBackoff).IsTrue();
    }

    [Test]
    public async Task Default_EnableBatchSending_should_be_true_for_efficient_transport()
    {
        var options = new OutboxProcessorOptions();

        _ = await Assert.That(options.EnableBatchSending).IsTrue();
    }
}
