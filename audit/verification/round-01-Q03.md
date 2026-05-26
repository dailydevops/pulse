# Q03 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:384` — `nextRetryAt = _options.ComputeNextRetryAt(DateTimeOffset.UtcNow, message.RetryCount);` (inside `ProcessMessageAsync`, used when an individual send fails and exponential backoff is enabled).
- `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:477` — `var now = DateTimeOffset.UtcNow;` (inside `ProcessBatchSendAsync`, used to compute `NextRetryAt` for every failed message in the batch).
- Grep over the whole file for `TimeProvider` or `_timeProvider`: no matches. The service does not accept a `TimeProvider` argument and does not have any field of that type.
- Contrast: `src/NetEvolve.Pulse/Idempotency/IdempotencyStore.cs:52-63` uses the injected `_timeProvider.GetUtcNow()` in both `ExistsAsync` and `StoreAsync`.

**Reasoning:**
The codebase already wires `TimeProvider` as a singleton service (`OutboxExtensions.cs:64` and `ServiceCollectionExtensions.cs:96`) and most other components honor it (Idempotency, OutboxEventStore, ActivityAndMetricsRequestInterceptor). The hosted service is the exception. Because `ComputeNextRetryAt` adds the computed delay to the supplied `now`, a fake clock pinned at e.g. `2030-01-01` will write a `NextRetryAt` that is essentially `DateTimeOffset.UtcNow + BaseRetryDelay` instead of `2030-01-01 + BaseRetryDelay`. That breaks any deterministic retry-window test and any "back-date the clock" scenario.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Outbox/OutboxProcessorTimeProviderTests.cs`
- Status: blocked-by-API

The minimum-exposure fix is to add a `TimeProvider` parameter to `OutboxProcessorHostedService`'s constructor (or pull it through `IOptions`), then call `_timeProvider.GetUtcNow()` at line 384 and line 477. The failing test below already encodes the desired behavior; it cannot pass today because there is no constructor parameter to receive a `FakeTimeProvider`.

The test writes a message, lets the hosted service fail it (failing transport), then asserts:
1. The persisted `NextRetryAt` is anchored at the fake-clock time (`2030-01-01`), not real wall-clock time.

Because the constructor cannot accept a `TimeProvider`, the test asserts the constructor has a `TimeProvider` parameter via reflection and fails — this captures the API gap.

**Notes:**
Phase 3 builders should also wire the field into the gauge subscription so the unhealthy-cycle back-off delay can be tested deterministically, but at minimum the two `DateTimeOffset.UtcNow` call sites must be replaced with `_timeProvider.GetUtcNow()`.
