---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse/Interceptors/AuditRequestInterceptor*.cs"

created: 2026-09-28

lastModified: 2026-09-29

state: accepted

instructions: |
  AuditRequestInterceptor MUST wrap only the handler call in try/catch; the audit record MUST reflect the handler outcome only (Success when it completed, Failure with the handler's exception message when it threw).
  Audit writes are best effort (fail open): exceptions from IAuditUserAccessor, IPayloadSerializer or IAuditStore.RecordAsync MUST be logged at Error level and MUST NOT change the handler outcome - a successful handler returns its response, a failing handler's original exception is rethrown unchanged.
  Once the handler has finished, IAuditStore.RecordAsync MUST be called with CancellationToken.None, not the caller's token.
---

# Decision: Audit Writes Are Best Effort (Fail Open)

The `AuditRequestInterceptor` records the handler outcome after the handler has run. A failure while building or persisting the audit record is logged and never changes what the caller sees.

## Context

Before this decision, the success-path audit write sat inside the same `try` block as the handler call. An `IAuditStore.RecordAsync` failure after a successful handler was caught and recorded as a second, `Failure` audit record carrying the store's exception message, and the store exception was then thrown to the caller. On the failure path, an exception from the serializer, the user accessor or the store escaped before `throw;` and replaced the handler exception. Both writes also used the caller's `CancellationToken`, so cancelling after the handler had finished discarded the record and reported a completed command as cancelled (issue #815).

A command that already committed its side effects and is then reported as failed invites callers to retry it, which is more harmful than a missing audit row. The class contract already promised that the original exception always propagates unchanged. The contract did not define what happens when the audit write itself fails, so that policy is recorded here.

## Decision

- Only the handler call is wrapped in `try`/`catch`. The audit record is built and written afterwards, with `AuditResult.Success` or `AuditResult.Failure` and the handler's exception message.
- Building and writing the record is best effort: exceptions from `IAuditUserAccessor.GetCurrentUser`, `IPayloadSerializer.Serialize` and `IAuditStore.RecordAsync` are logged at `Error` level with the request name, the attempted `AuditResult` and the correlation id, and are then discarded.
- A successful handler returns its response even when the audit write fails. A failing handler's original exception is rethrown unchanged.
- The audit write uses `CancellationToken.None`, because the handler has already finished.

## Consequences

- Callers never see audit-store errors, and completed commands are never reported as failed or cancelled because of auditing.
- An audit record can be missing when the store is unavailable. Operators detect this through the `Error` log entry, not through a failed request.
- A store that hangs blocks the request, because the write no longer observes the caller's token. Store implementations should apply their own command timeouts.
- `AuditRequestInterceptor` now depends on `ILogger<T>`, which every host that uses `AddPulse()` already provides for `PulseMediator`.

## Alternatives Considered

- **Fail closed (propagate the store exception on the success path).** Suits strict compliance setups, but it reports a committed command as failed and invites unsafe retries. Rejected as the default.
- **Make the policy configurable via `AuditOptions`.** Adds an option nobody has asked for yet. It can be added later without breaking this default.
- **Short independent timeout for the audit write.** Adds a second, arbitrary timeout value; provider command timeouts already cover it.

## Related Decisions (Optional)

None at this time.
