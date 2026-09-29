---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.Extensibility/Idempotency/*.cs"
  - "src/NetEvolve.Pulse/Idempotency/*.cs"
  - "src/**/Idempotency/*.cs"
  - "src/**/Scripts/IdempotencyKey.sql"

created: 2026-09-28

lastModified: 2026-09-28

state: proposed

instructions: |
  IdempotencyStore MUST reserve and store keys through IIdempotencyKeyRepository.TryReserveAsync(key, createdAt, validFrom), never through ExistsAsync followed by StoreAsync.
  Every IIdempotencyKeyRepository implementation MUST make TryReserveAsync one atomic operation: insert an absent key, overwrite the timestamp of a key created before validFrom, leave a key that has not expired untouched, and return true for exactly one of several concurrent callers.
  With validFrom = null (TimeToLive = null), an existing key MUST never be modified.
---

# Decision: Refresh Expired Idempotency Keys on Reserve

Idempotency keys that have outlived `IdempotencyKeyOptions.TimeToLive` are refreshed, not left in place, when they are reserved or stored again. The check and the refresh happen in one atomic repository operation.

## Context

With a `TimeToLive`, `ExistsAsync` treats a key older than the cutoff as absent, and the library never deletes rows. Reservation used the default `IIdempotencyStore.TryReserveAsync` (`ExistsAsync` followed by `StoreAsync`), and every backend implemented `StoreAsync` as insert-if-absent. An expired key therefore kept its old timestamp forever. Every later duplicate of that key ran the handler again (#814). Refreshing the key needs the cutoff, but `IIdempotencyKeyRepository.StoreAsync` never receives it. A separate exists check followed by a write is also not race-safe.

## Decision

- `IIdempotencyKeyRepository` gets `Task<bool> TryReserveAsync(string idempotencyKey, DateTimeOffset createdAt, DateTimeOffset? validFrom, CancellationToken)`. It has no default implementation, in line with the pre-1.0 interface evolution decision.
- `IdempotencyStore` overrides `TryReserveAsync`, and routes `StoreAsync` through it, so `ExistsAsync` returns `true` after either call.
- Each provider implements the operation atomically with the tools of its backend:
  - SQL Server: the `usp_ReserveIdempotencyKey` stored procedure with `MERGE ... WITH (HOLDLOCK)` and `WHEN MATCHED AND CreatedAt < @validFrom THEN UPDATE`.
  - PostgreSQL: the `fn_reserve_idempotency_key` function with `ON CONFLICT ... DO UPDATE ... WHERE created_at < p_valid_from`. It is a new function because `CREATE OR REPLACE` cannot change the return type of `fn_insert_idempotency_key`.
  - SQLite: `ON CONFLICT ... DO UPDATE ... WHERE`.
  - MySQL: `INSERT IGNORE` followed by a conditional `UPDATE`, and one more `INSERT IGNORE` when the `UPDATE` matches no row, because a cleanup job can delete the expired row between the two statements. `ON DUPLICATE KEY UPDATE` is not used, because with the driver's default found-rows mode it reports the same row count for an insert and for an untouched duplicate.
  - Entity Framework Core: `ExecuteDeleteAsync` of the expired row followed by the regular insert, whose primary key conflict returns `false`. `ExecuteUpdateAsync` is not used, because the Oracle MySQL provider cannot bind converted `DateTimeOffset` values in setters. InMemory uses change tracking, since it supports neither bulk operation.
  - Redis: `SET NX` without a cutoff, and a Lua script with a cutoff that compares UTC round-trip timestamps and resets the expiry. Values with a non-UTC offset are treated as present, so a live key is never overwritten.

## Consequences

- A retried command is rejected again after its key has been re-reserved, in every provider.
- External implementers of `IIdempotencyKeyRepository` must implement the new member.
- Deployments of the SQL Server and PostgreSQL providers must re-run `IdempotencyKey.sql` together with the package upgrade.
- `IIdempotencyKeyRepository.StoreAsync` is no longer called by the library. It stays on the interface for direct callers.
- The Redis provider needs the scripting commands (`EVAL`, `EVALSHA`) once a `TimeToLive` is set. Redis values with a non-UTC offset, written by earlier versions through direct `StoreAsync` calls, stay unreservable until their physical expiry.

## Alternatives Considered

- **Refresh inside `StoreAsync`:** needs the cutoff as a new parameter, which is also a signature change, and keeps the non-atomic exists-then-store reservation.
- **Default interface implementation of `TryReserveAsync`:** any default would be exists-then-store and would silently keep the bug for external implementers.
- **Delete expired rows in the library:** needs a background job and still leaves a window between cleanup runs.

## Related Decisions

- [Extensibility Interface Evolution Before 1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - governs how the new repository member is added and announced.
- [DateTimeOffset and TimeProvider Usage](./2026-01-21-datetimeoffset-and-timeprovider-usage.md) - the timestamps and the cutoff come from the injected `TimeProvider`.
