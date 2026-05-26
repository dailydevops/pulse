# Q04 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse/Interceptors/IdempotencyCommandInterceptor.cs:68-79`:
  ```csharp
  var key = idempotentCommand.IdempotencyKey;
  if (await store.ExistsAsync(key, cancellationToken).ConfigureAwait(false))
  {
      throw new IdempotencyConflictException(key);
  }
  var result = await handler(request, cancellationToken).ConfigureAwait(false);
  await store.StoreAsync(key, cancellationToken).ConfigureAwait(false);
  ```
- `src/NetEvolve.Pulse/Idempotency/IdempotencyStore.cs:47-64` — `ExistsAsync` and `StoreAsync` are wholly separate operations; the contract has no put-if-absent / atomic-claim semantics.
- `src/NetEvolve.Pulse.Extensibility/Idempotency/IIdempotencyStore.cs` — interface contains only `ExistsAsync(key, ct)` and `StoreAsync(key, ct)`. No `TryStoreAsync` / `ClaimAsync`.

**Reasoning:**
The interceptor is a classic TOCTOU pattern. Under concurrent dispatch of two `IIdempotentCommand`s with the same key:
1. Thread A: `ExistsAsync(k) -> false`
2. Thread B: `ExistsAsync(k) -> false`  (because A has not stored yet)
3. Thread A: handler runs (side effects)
4. Thread B: handler runs (DUPLICATE side effects)
5. Both call `StoreAsync(k)` — the second is a no-op or silent overwrite.

The public claim "at-most-once execution" (see XML docs on `IdempotencyCommandInterceptor`) is only true for strictly sequential duplicates.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Interceptors/IdempotencyConcurrentRaceTests.cs`
- Status: written

The test:
1. Constructs two `IdempotencyCommandInterceptor<TestCommand, string>` instances sharing the same `TrackingIdempotencyStore`.
2. Each schedules a `HandleAsync` for the same idempotency key with a gated handler (`SemaphoreSlim`/`TaskCompletionSource`) that only releases once both handlers have entered.
3. Asserts: `handler ran exactly once` AND `the second call surfaced IdempotencyConflictException`.

This fails today because both handlers fully execute before either calls `StoreAsync`, so `handlerExecutionCount == 2` and no `IdempotencyConflictException` is raised.

**Notes:**
The minimum-exposure fix is to add an atomic `TryStoreAsync(string key, CancellationToken)` (returns `bool`) on `IIdempotencyStore` (with a default-interface implementation falling back to the racey ExistsAsync+StoreAsync sequence for back-compat) and reorder the interceptor: `if (!await store.TryStoreAsync(key, ct)) throw new IdempotencyConflictException(key); try { return await handler(...); } catch { await store.RemoveAsync(key, ct); throw; }` — or similar. The Phase 3 builder should pick the design.
