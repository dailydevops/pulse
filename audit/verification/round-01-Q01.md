# Q01 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:93-119` — constructor takes `IOutboxRepository` directly; no `IServiceScopeFactory` or `IServiceProvider` is injected (verified via `Grep` — no matches for `IServiceScopeFactory|CreateScope|IServiceProvider` in the file).
- `src/NetEvolve.Pulse/OutboxExtensions.cs:73` — `services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxProcessorHostedService>());`
- `src/NetEvolve.Pulse.SqlServer/SqlServerExtensions.cs:199-202` — `.AddScoped<IOutboxRepository, SqlServerOutboxRepository>()`
- `src/NetEvolve.Pulse.EntityFramework/EntityFrameworkExtensions.cs:66` — `.AddScoped<IOutboxRepository, EntityFrameworkOutboxRepository<TContext>>()`
- `src/NetEvolve.Pulse.EntityFramework/Outbox/EntityFrameworkOutboxRepository.cs:41-47` — repository takes `TContext` (the EF DbContext) which is itself scoped.

**Reasoning:**
A singleton hosted service that takes a scoped dependency through its constructor is a textbook captive-dependency bug. The DI container enforces this when `ValidateScopes=true` (default in dev). For EF-backed repositories the captured `DbContext` is held for the lifetime of the host, accumulates change-tracker state forever, and is not thread-safe. The ADO.NET implementations mask the symptom because they open a fresh `DbConnection` per method call, but the lifetime registration is still wrong by contract.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Outbox/SingletonScopedCaptiveDependencyTests.cs`
- Status: written

The test calls `services.BuildServiceProvider(validateScopes: true)` and asks the container to resolve the registered `IHostedService` enumerable. Today this throws `InvalidOperationException` only when host startup runs validation; we trigger the validation path explicitly. The fix the Phase 3 builder must apply is to either (a) make `OutboxProcessorHostedService` create a scope per polling cycle via `IServiceScopeFactory`, or (b) register `IOutboxRepository` as Singleton (only safe for stateless connection-per-call ADO.NET impls — not EF).

**Notes:**
The currently registered SQL Server / EF Core impls all use Scoped lifetime; the singleton-hosted-service rule will fail for all of them under `ValidateOnBuild`/`ValidateScopes`. The failing test below targets the EF path because it is the most dangerous (captures a non-thread-safe DbContext). Phase 3 builders MUST NOT "fix" the captive by removing `ValidateScopes` — the right fix is a per-cycle scope.
