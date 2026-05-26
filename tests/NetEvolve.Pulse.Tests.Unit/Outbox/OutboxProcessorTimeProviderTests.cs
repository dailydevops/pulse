namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Phase 2 audit verification — Q03.
/// <see cref="OutboxProcessorHostedService"/> calls <c>DateTimeOffset.UtcNow</c>
/// directly at lines 384 and 477, bypassing the rest-of-the-codebase
/// <see cref="TimeProvider"/> convention. This prevents deterministic
/// retry-window assertions with <c>FakeTimeProvider</c>.
///
/// BLOCKED-BY-API today: the public constructor does not accept a
/// <see cref="TimeProvider"/>, so a follow-up test cannot inject a fake clock.
/// Phase 3 must add the parameter and reroute the two call sites.
/// </summary>
[TestGroup("Audit-Q03")]
public sealed class OutboxProcessorTimeProviderTests
{
    [Test]
    public async Task OutboxProcessorHostedService_Should_Accept_A_TimeProvider_Argument()
    {
        var ctors = typeof(OutboxProcessorHostedService).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        var acceptsTimeProvider = ctors.Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(TimeProvider)));

        // ASSERTION CAPTURES THE DEFECT:
        // The hosted service must accept a TimeProvider so that retry scheduling
        // honors the same fake clock used by IdempotencyStore, OutboxEventStore, etc.
        _ = await Assert.That(acceptsTimeProvider).IsTrue();
    }
}
