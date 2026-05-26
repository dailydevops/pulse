# U06 Verification

**Status:** CONFIRMED

**Evidence:** `src/NetEvolve.Pulse/Outbox/OutboxProcessorOptions.cs:28` (`MaxRetryCount = 3`), `src/NetEvolve.Pulse/Outbox/OutboxProcessorOptions.cs:40` (`EnableBatchSending { get; set; }` — implicit `false`), `src/NetEvolve.Pulse/Outbox/OutboxProcessorOptions.cs:47` (`EnableExponentialBackoff { get; set; }` — implicit `false`).

**Reasoning:** Re-read the type. Three options govern retry/backoff behavior on the headline reliability surface (`AddOutbox`). All three are foot-gun defaults: 3 retries (low for transient broker failures), exponential backoff disabled (retries fire back-to-back gated only by `PollingInterval`), and batching disabled (forces per-message round trips). A first-time user calling `AddOutbox()` with no overrides will dead-letter messages on any non-trivial transient outage. The failing test asserts the **desirable** defaults (backoff `true`, batching `true`, retry count `>= 5`) so Phase 3 can flip them; if Phase 3 decides to retain current defaults, the test becomes the documented-choice contract test.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Outbox/OutboxProcessorOptionsDefaultsTests.cs`
- Status: written

```csharp
namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Phase 2 audit U06: Asserts the *desirable* defaults for <see cref="OutboxProcessorOptions"/>.
/// Currently FAILS — defaults are configured as a foot-gun:
/// MaxRetryCount=3, EnableExponentialBackoff=false, EnableBatchSending=false.
/// Phase 3 should either flip these to the safer values asserted below
/// or, if the current values are intentional, repurpose this test (and update its
/// assertions/message) as the documented-choice contract test.
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
```

**Notes:**
- Each of the three assertions fails today (3 < 5; `false` != `true`; `false` != `true`).
- Phase 3 deliverable: either flip the defaults (changes are one-line edits in `OutboxProcessorOptions.cs:28,40,47`) or document the rationale for foot-gun defaults and rewrite this test as the contract test.
- Per-event-type override plumbing (`EventTypeOverrides`, `GetEffectiveMaxRetryCount`, `GetEffectiveEnableBatchSending`) is unaffected by any default flip — overrides remain `null` until explicitly set, so changing globals is safe.
- If Phase 3 raises `MaxRetryCount`, also reconsider `MaxRetryDelay` (currently 5 min) to ensure the upper bound is still reachable in practice.
