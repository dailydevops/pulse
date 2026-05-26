namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using TUnit.Core;

/// <summary>
/// Phase 2 audit verification — Q02.
/// The outbox state machine flips Pending→Processing inside <c>GetPendingAsync</c>.
/// If the host crashes between fetch and <c>MarkAsCompleted/Failed/DeadLetter</c>,
/// the row sits in Processing forever — neither <c>GetPendingAsync</c> (Status=0 only)
/// nor <c>GetFailedForRetryAsync</c> (Status=3 only) will surface it.
///
/// EXPECTED TO FAIL today: the test asserts that <see cref="IOutboxRepository"/>
/// exposes some way to reclaim stuck-Processing rows. No such method exists.
/// </summary>
[TestGroup("Audit-Q02")]
public sealed class StuckProcessingMessagesReaperTests
{
    [Test]
    public async Task IOutboxRepository_Should_Expose_A_Reaper_For_Stuck_Processing_Rows()
    {
        var contract = typeof(IOutboxRepository);
        var methods = contract.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        // A reaper method must exist. We accept any of these conventional names; the Phase 3 fix may pick one.
        var hasReaper = methods.Any(m =>
            string.Equals(m.Name, "ReclaimStuckProcessingAsync", StringComparison.Ordinal)
            || string.Equals(m.Name, "ReclaimStuckAsync", StringComparison.Ordinal)
            || string.Equals(m.Name, "GetStuckProcessingAsync", StringComparison.Ordinal)
            || string.Equals(m.Name, "RescueStuckAsync", StringComparison.Ordinal)
        );

        // ASSERTION CAPTURES THE DEFECT:
        // The at-least-once delivery contract requires a way to reclaim rows whose host died mid-process.
        // No such API exists on IOutboxRepository today.
        _ = await Assert.That(hasReaper).IsTrue();
    }
}
