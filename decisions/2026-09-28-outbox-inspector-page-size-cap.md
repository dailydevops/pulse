---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.AspNetCore/Outbox/**/*.cs"

created: 2026-09-28

lastModified: 2026-09-28

state: accepted

instructions: |
  MUST reject pageSize values outside 1..1000 on GET {BasePath}/messages and GET {BasePath}/dead-letters of MapOutboxInspector with 400 Bad Request and a validation problem body keyed "pageSize", before IOutboxManagement is called.
  MUST NOT add the cap to IOutboxManagement or its providers; programmatic callers may request larger pages.
---

# Decision: Outbox Inspector Page Size Cap

The outbox inspector listing endpoints accept at most 1000 messages per page. The limit applies at the HTTP boundary only and matches the other inspectors in `NetEvolve.Pulse.AspNetCore`.

## Context

Issue #849 shows that `GET {BasePath}/messages?pageSize=2147483647&page=0` passed validation. The provider then loaded every outbox message, payload included, and the endpoint serialized all of them into one response. This is unrestricted resource consumption as described in OWASP API Security Top 10 2023, API4:2023.

[Outbox Inspection and Dead-Letter Dismissal](./2026-09-24-outbox-inspection-and-dead-letter-dismissal.md) lists the rejected paging values as `pageSize < 1`, `page < 0` and `page * pageSize > int.MaxValue`, with no upper bound. The audit inspector (`take`, maximum 1000) and the command dead-letter inspector (`count`, maximum 1000) already cap their page size.

## Decision

- `OutboxInspectorEndpoints` defines `MaxPageSize = 1000`. The shared paging validation of `GET {BasePath}/messages` and `GET {BasePath}/dead-letters` rejects `pageSize` below 1 or above 1000 with `400 Bad Request` and the validation error "The page size must be between 1 and 1000.".
- The existing rules for `page` stay unchanged: it must not be negative, and `page * pageSize` must not exceed `int.MaxValue`.
- `IOutboxManagement` and its providers keep their contract. They validate only the lower bound and the offset overflow.

## Consequences

- One request can no longer load and serialize the whole outbox.
- Clients that requested more than 1000 messages per page now receive `400 Bad Request` and must page instead.
- The limit is a constant, like in the sibling inspectors. A configurable limit can be added when an operator needs a different value.

## Alternatives Considered

- **Clamp larger values to 1000:** Rejected, because the caller would silently receive fewer messages than requested. The sibling inspectors reject instead.
- **Enforce the limit in the providers:** Rejected, because programmatic callers may legitimately request larger pages, and it would change seven providers.
- **Make the limit configurable in `OutboxInspectorOptions`:** Deferred. No operator has asked for it, and the sibling inspectors use constants.

## Related Decisions

- [Outbox Inspection and Dead-Letter Dismissal](./2026-09-24-outbox-inspection-and-dead-letter-dismissal.md) - Defines the endpoints and the other paging rules; this decision adds the endpoint-level upper bound.
