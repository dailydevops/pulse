# Q05 Verification

**Status:** NEEDS-NUANCE

**Evidence:**
- `src/NetEvolve.Pulse/Interceptors/ConcurrentCommandGuardInterceptor.cs:45` — `private readonly ConcurrentDictionary<Type, SemaphoreSlim> _semaphores = new();`
- `src/NetEvolve.Pulse/Interceptors/ConcurrentCommandGuardInterceptor.cs:57` — `var semaphore = _semaphores.GetOrAdd(typeof(TRequest), _ => new SemaphoreSlim(1, 1));`
- `src/NetEvolve.Pulse/Interceptors/ConcurrentCommandGuardInterceptor.cs:59` — `await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);`
- `src/NetEvolve.Pulse/Interceptors/ConcurrentCommandGuardInterceptor.cs:71-84` — `Dispose()` iterates and disposes every value in `_semaphores`, then clears. No coordination with in-flight `WaitAsync` callers.

**Reasoning:**
Two sub-claims:

1. **Dispose race → ODE during shutdown:** TRUE. `SemaphoreSlim.WaitAsync` (lines 59) does not honor disposal of the semaphore as cancellation. If `Dispose()` runs on the DI shutdown thread while another thread is parked in `WaitAsync(cancellationToken)`, the parked thread surfaces `ObjectDisposedException`, not `OperationCanceledException`. This is observable by writing a test that disposes the interceptor while a handler holds the semaphore. ODE during graceful shutdown is misleading: callers expect `OCE` and may catch it as the cancellation contract.

2. **`GetOrAdd` factory leak:** TRUE but mitigated by SemaphoreSlim's design — `SemaphoreSlim` allocates a lazy `AvailableWaitHandle` only on first call. The discarded instances created by losing-race factories are GC'd. They are not tracked in `_semaphores`, so they are NEVER disposed deterministically — but because the wait handle is lazy, the practical leak is small (one managed `SemaphoreSlim` object per racing call). Under heavy contention the discarded objects can accumulate briefly until GC reclaims them. This is a correctness smell more than a runtime crash.

Together: the higher-impact bug is the dispose race; the factory-leak is real but minor.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Interceptors/ConcurrentCommandGuardDisposeRaceTests.cs`
- Status: written

The test parks one `HandleAsync` invocation in `WaitAsync` (the inner handler awaits a `TaskCompletionSource` that is never released), disposes the interceptor on another thread, then awaits the parked task. The parked task today surfaces `ObjectDisposedException`. The assertion requires `OperationCanceledException` (the contract for shutdown), so it fails.

**Notes:**
For the factory leak claim, a deterministic reflection-based test is brittle (`GetOrAdd` factory race is timing-dependent and the .NET implementation does not guarantee multi-invocation under any specific contention pattern). We omit a failing test for that sub-claim and recommend Phase 3 fix `GetOrAdd` via the `factoryArgument` overload + `Lazy<SemaphoreSlim>` value, *or* use `ConcurrentDictionary<Type, Lazy<SemaphoreSlim>>` so the inner allocation runs exactly once.

For the dispose race, the right Phase 3 fix is to add an internal `CancellationTokenSource` linked to disposal and link it into the `WaitAsync` call (`semaphore.WaitAsync(linkedToken)`), then cancel that source from `Dispose()` before disposing the semaphores. That converts the race into the expected `OperationCanceledException`.
