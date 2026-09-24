---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.Extensibility/Outbox/IOutboxManagement.cs"
  - "src/**/Outbox/*OutboxManagement*.cs"
  - "src/**/Scripts/OutboxMessage.sql"
  - "src/NetEvolve.Pulse.AspNetCore/Outbox/**/*.cs"

created: 2026-09-24

lastModified: 2026-09-24

state: proposed

instructions: |
  MUST keep IOutboxManagement.GetMessagesAsync and GetMessageAsync read-only; MUST NOT reuse IOutboxRepository.GetPendingAsync or any other method that changes message status for inspection.
  MUST implement dead-letter dismissal as a permanent delete restricted to Status = DeadLetter, returning false when no dead letter matches; MUST NOT add a Dismissed status or mark dismissed messages as Completed.
  MUST treat "POST {BasePath}/messages/{id}/replay" as an alias of the dead-letter replay (dead letters only).
  MUST reject invalid paging (pageSize < 1, page < 0, page * pageSize > int.MaxValue) with ArgumentOutOfRangeException in providers and 400 Bad Request in the inspector endpoints.
  MUST add new IOutboxManagement members as regular interface members (no default implementations) while the package is below 1.0 and document the addition for external implementers.
---

# Decision: Outbox Inspection and Dead-Letter Dismissal

Extend `IOutboxManagement` with a read-only message listing, a single-message lookup and a dead-letter dismissal that permanently deletes the message. Expose them through the outbox inspector Minimal API endpoints.

## Context

Issue #290 asks for Minimal API endpoints to inspect outbox messages in production, including listing messages in any status, reading a single message, replaying a message and dismissing dead letters. Before this decision, `IOutboxManagement` only offered dead-letter operations (list, count, get, replay, replay all) and statistics.

The following constraints apply:

- `IOutboxRepository.GetPendingAsync` locks the returned messages by moving them to `Processing`. An inspection endpoint built on it would steal work from the outbox processor. Inspection therefore needs a dedicated, read-only method.
- `OutboxMessageStatus` has no `Dismissed` value, and every provider persists the status as an integer without a check constraint. A new status value would change statistics (`OutboxStatistics`), all provider queries and the documentation of every status-based cleanup.
- Seven providers implement `IOutboxManagement`: Entity Framework, SQL Server, PostgreSQL, MySQL, SQLite, MongoDB and Cosmos DB. SQL Server and PostgreSQL access the table through stored procedures and functions shipped in `Scripts/OutboxMessage.sql`.
- The package is below version 1.0. GitVersion bumps the major version for breaking-change markers, which the project does not want for interface additions.

## Decision

- Add `GetMessagesAsync(int pageSize = 50, int page = 0, OutboxMessageStatus? status = null, CancellationToken cancellationToken = default)`. It returns messages in any status, or only messages in `status` when set. Results are ordered by `UpdatedAt` descending. Where the store supports it cheaply, providers add `Id` descending as a tie-breaker. The method never changes message state.
- Add `GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)`. It returns the message in any status, or `null` when it does not exist.
- Add `DismissMessageAsync(Guid messageId, CancellationToken cancellationToken = default)`. It permanently deletes the message only if its status is `DeadLetter`. It returns `true` when a dead letter was deleted and `false` otherwise, so the endpoint can answer `404 Not Found`.
- Validate paging the same way in every provider, following the existing Entity Framework guard. `pageSize` must be greater than zero, `page` must not be negative, and `page * pageSize` must not exceed `int.MaxValue`. Violations throw `ArgumentOutOfRangeException`.
- Follow each provider's existing access pattern. SQL Server and PostgreSQL get new stored procedures and functions in `Scripts/OutboxMessage.sql` (`usp_GetOutboxMessages`, `usp_GetOutboxMessage`, `usp_DismissOutboxMessage`, `get_outbox_messages`, `get_outbox_message`, `dismiss_outbox_message`). MySQL and SQLite use cached inline SQL. MongoDB and Cosmos DB use their SDK queries with the Guid encoding each store already uses.
- Endpoints in `OutboxInspectorEndpoints.MapOutboxInspector`:
  - `GET {BasePath}/messages` uses the query parameters `pageSize`, `page` and `status`. These match the existing `dead-letters` listing rather than the `count`/`skip` names in the issue.
  - `GET {BasePath}/messages/{id:guid}` returns `200` or `404`.
  - `POST {BasePath}/messages/{id:guid}/replay` is an alias of `POST {BasePath}/dead-letters/{id:guid}/replay` and only replays dead letters.
  - `POST {BasePath}/dead-letters/{id:guid}/dismiss` returns `204` or `404`.
  - Invalid paging and undefined status values return `400 Bad Request` with a validation problem body instead of reaching the provider.
- Add the members as regular interface members, not as default interface methods. List the addition under "Impact" in the pull request so external implementers can react.

## Consequences

- Operators can inspect stuck `Pending`, `Processing` or `Failed` messages without interfering with the outbox processor.
- Dismissal is irreversible. The endpoint only deletes dead letters, and callers MUST secure the inspector group, for example with `RequireAuthorization()`.
- Statistics stay truthful. Dismissed messages disappear instead of being counted as `Completed`.
- External implementations of `IOutboxManagement` no longer compile until they implement the three new members.
- SQL Server and PostgreSQL users MUST re-run the idempotent `Scripts/OutboxMessage.sql` after upgrading, otherwise the new operations fail with a missing procedure or function.
- The generic `messages` listing uses offset paging. Deep pages become slower on large tables, which is acceptable for an administrative endpoint.

## Alternatives Considered

- **Mark dismissed messages as `Completed`:** This needs no delete, and the existing retention cleanup removes the rows eventually. It was rejected because it misreports undelivered messages as delivered in statistics and monitoring.
- **Add `OutboxMessageStatus.Dismissed`:** This keeps an audit trail. It was rejected because it changes statistics, the status documentation and every provider query, and needs its own cleanup path. It can be revisited if auditing dismissed messages becomes a requirement.
- **Reuse `IOutboxRepository.GetPendingAsync` for the listing:** Rejected, because it changes message status to `Processing` and competes with the outbox processor.
- **Widen `messages/{id}/replay` to `Failed` messages:** Rejected, because it changes the replay semantics of all providers. Failed messages are retried automatically by the processor.
- **Default interface methods throwing `NotSupportedException`:** Rejected, because they hide missing functionality until runtime. The package is pre-1.0, so a compile-time signal is preferred.
- **Inline SQL instead of new stored procedures and functions for SQL Server and PostgreSQL:** This avoids re-running the script. It was rejected for consistency with the existing management operations of these providers.

## Related Decisions

- [DateTimeOffset and TimeProvider Usage](./2026-01-21-datetimeoffset-and-timeprovider-usage.md) - Timestamps written by management operations use `TimeProvider`.
- [GitVersion Automated Semantic Versioning](./2025-07-10-gitversion-automated-semantic-versioning.md) - Explains why the interface addition is released as a regular feature below 1.0.
