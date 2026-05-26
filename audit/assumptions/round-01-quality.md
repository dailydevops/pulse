# Phase 1 — Round 01 — Quality Discovery

> Read-only assumptions. Each must be confirmed or refuted in Phase 2 with file:line evidence.

## Repo Snapshot
19 src projects: core (`NetEvolve.Pulse`), `Extensibility`, source generator, AspNetCore, EF Core, 4 ADO.NET providers (SqlServer, PostgreSql, SQLite, plus EF-backed MySql/CosmosDb/MongoDB), 3 native transports (Kafka, RabbitMQ, AzureServiceBus), 3 cross-cutting (Polly, FluentValidation, HttpCorrelation), Dapr, Redis, MySql. Core abstractions: `IMediator` (`PulseMediator` in `Internals/`), open-generic interceptors per request kind, `IEventDispatcher` strategies (Parallel/Sequential/Prioritized/RateLimited), outbox subsystem split into `IEventOutbox` + `IOutboxRepository` + `IMessageTransport` + `OutboxProcessorHostedService`. Outbox state machine driven by stored procedures/functions (`get_pending_outbox_messages` flips Pending→Processing using FOR UPDATE SKIP LOCKED), batch defaults `EnableBatchSending=false`, `EnableExponentialBackoff=false`, `MaxRetryCount=3`. TimeProvider is wired through most code; outbox processor leaks back to `DateTimeOffset.UtcNow` in two spots.

## Assumptions

### Q01 — Singleton hosted service consumes scoped IOutboxRepository (captive dependency)
- Claim: `OutboxProcessorHostedService` is registered Singleton but constructor-injects `IOutboxRepository`, which is registered Scoped by SQL Server/PostgreSql/SQLite/EF extensions. No `IServiceScopeFactory.CreateScope()` is created per polling cycle.
- Evidence: `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:93-119` (constructor takes `IOutboxRepository`), `src/NetEvolve.Pulse/OutboxExtensions.cs:72-73` (`Singleton<IHostedService, OutboxProcessorHostedService>`), `src/NetEvolve.Pulse.SqlServer/SqlServerExtensions.cs:199-202` (`AddScoped<IOutboxRepository, SqlServerOutboxRepository>`), `src/NetEvolve.Pulse.EntityFramework/Outbox/EntityFrameworkOutboxRepository.cs:41-47` (takes `TContext`).
- Why it matters: Captured EF `DbContext` is not thread-safe, accumulates change-tracker state forever, prevents per-poll connection reuse. ADO.NET impls mask this (fresh connections per call); EF-backed users will hit `ObjectDisposedException`/concurrency exceptions or unbounded memory growth.
- Test idea: Register `AddEntityFrameworkOutbox<TestDb>()`, build host with `ValidateScopes=true`, assert startup throws `InvalidOperationException` for the singleton-consuming-scoped registration; or assert same `IOutboxRepository`/`DbContext` instance across two simulated polling cycles.

### Q02 — Stuck "Processing" rows never recovered (broken at-least-once)
- Claim: Outbox processor flips messages to `Status=1` (Processing) in the fetch query, but has no reaper for rows stuck in Processing if the host dies between fetch and `MarkAsCompleted/Failed/DeadLetter`. Pending-fetch and retry-fetch only look at `Status=0` and `Status=3`.
- Evidence: `src/NetEvolve.Pulse.PostgreSql/Scripts/OutboxMessage.sql:78-105` (UPDATE…SET Status=1 inside `get_pending_outbox_messages`), `:131-162`, `:180-181`, `:202-203`; `src/NetEvolve.Pulse.SqlServer/Outbox/SqlServerOutboxRepository.cs:163-199`; `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:236-292` (no reaper).
- Why it matters: Breaks at-least-once guarantee on crash/OOM/SIGKILL. Rows stay Processing forever; `pulse.outbox.pending` metric will not see them.
- Test idea: Insert one Pending row, call `GetPendingAsync`, simulate crash (skip MarkAsCompleted), restart host, assert second `GetPendingAsync` returns the stuck row (currently does not).

### Q03 — OutboxProcessorHostedService bypasses TimeProvider
- Claim: Uses `DateTimeOffset.UtcNow` directly instead of injected `TimeProvider` (which the rest of the codebase honors: `IdempotencyStore`, `OutboxEventStore`, `ActivityAndMetricsRequestInterceptor`).
- Evidence: `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:384` (`_options.ComputeNextRetryAt(DateTimeOffset.UtcNow, …)`), `:477` (`var now = DateTimeOffset.UtcNow;`). Compare `src/NetEvolve.Pulse/Idempotency/IdempotencyStore.cs:52` using `_timeProvider.GetUtcNow()`.
- Why it matters: Backoff schedules computed with a fake `TimeProvider` are not honored; deterministic retry-window assertions impossible; persisted `NextRetryAt` will not match the test clock.
- Test idea: Inject `FakeTimeProvider` fixed at 2030-01-01, force one message to fail with `EnableExponentialBackoff=true`, assert persisted `NextRetryAt = 2030-01-01 + BaseRetryDelay`.

### Q04 — IdempotencyCommandInterceptor is check-then-act, not atomic
- Claim: `ExistsAsync` → handler → `StoreAsync`. Two concurrent commands with the same key both pass `ExistsAsync` before either calls `StoreAsync` — handler executes twice.
- Evidence: `src/NetEvolve.Pulse/Interceptors/IdempotencyCommandInterceptor.cs:68-79`; `src/NetEvolve.Pulse/Idempotency/IdempotencyStore.cs:47-64` (no atomic put-if-absent in contract).
- Why it matters: Claims at-most-once execution but only catches sequential duplicates.
- Test idea: Two `IIdempotentCommand`s, same key, gated handler, `Task.WhenAll`; assert handler ran once and the second call surfaced `IdempotencyConflictException`.

### Q05 — ConcurrentCommandGuardInterceptor: dispose race + GetOrAdd factory leak
- Claim: Singleton interceptor with `Dispose()` walking `_semaphores`. Mid-shutdown awaits surface `ObjectDisposedException` instead of `OperationCanceledException`. `GetOrAdd(typeof(TRequest), _ => new SemaphoreSlim(1,1))` can leak losing-race semaphores.
- Evidence: `src/NetEvolve.Pulse/Interceptors/ConcurrentCommandGuardInterceptor.cs:45,49-68,71-84`.
- Why it matters: ODE during shutdown masks real cancellation; rare contention leaks under heavy load.
- Test idea: `Parallel.For` to provoke factory races and reflectively assert all created semaphores are present; await semaphore in HandleAsync, dispose on another thread, assert surfaced exception is `OperationCanceledException`.

### Q06 — KafkaMessageTransport ignores CancellationToken on Flush + IsHealthy
- Claim: `_producer.Flush(Timeout.InfiniteTimeSpan)` blocks indefinitely and ignores the passed token; `IsHealthyAsync` does sync `_adminClient.GetMetadata(TimeSpan.FromSeconds(5))` wrapped in `Task.FromResult`.
- Evidence: `src/NetEvolve.Pulse.Kafka/Outbox/KafkaMessageTransport.cs:94`, `:63-102`, `:105-116`.
- Why it matters: `IHostApplicationLifetime` cannot interrupt a stuck flush — graceful shutdown becomes ungraceful, increasing stuck-Processing risk (Q02).
- Test idea: Fake `IProducer<…>` whose `Flush` blocks forever, `SendBatchAsync` with token canceling after 100 ms; assert the task completes (it will not).

### Q07 — Azure Service Bus non-atomic batch when EnableBatching=false
- Claim: `SendBatchAsync` loops `sender.SendMessageAsync` per message when batching disabled — non-atomic. `ProcessBatchSendAsync` then marks entire batch failed on any error → re-send next poll → duplicate delivery.
- Evidence: `src/NetEvolve.Pulse.AzureServiceBus/Outbox/AzureServiceBusMessageTransport.cs:55-89`; `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:456-500`.
- Why it matters: Violates `IMessageTransport.SendBatchAsync` atomicity contract used by the processor.
- Test idea: Fake `ServiceBusSender` succeeds for messages 1+2 then throws on 3, run with `EnableBatching=false`; verify broker received first two, outbox marks all three Failed (next poll → duplicates).

### Q08 — RabbitMqMessageTransport channel leak on reconnect
- Claim: `EnsureChannelAsync` overwrites stale `_channel` without disposing the previous instance — leaks an `IRabbitMqChannelAdapter` and its AMQP channel per reconnect.
- Evidence: `src/NetEvolve.Pulse.RabbitMQ/Outbox/RabbitMqMessageTransport.cs:124-147`.
- Why it matters: Long-running services exhaust the connection's `channel_max` budget on transient disconnects → `ChannelAllocationException`.
- Test idea: Fake adapter that returns distinct disposable instances and flips `IsOpen=false` after each publish; send 100 messages, assert previous adapters were disposed.

### Q09 — Batch mark operations are not atomic (per-row + parallel)
- Claim: Default-interface batch methods on `IOutboxRepository` iterate via `Parallel.ForEachAsync` to single-item overloads (fresh connections, no transaction). PostgreSQL override iterates sequentially under one connection but no transaction. A transient mid-loop failure leaves some Completed, others Processing → duplicates next poll.
- Evidence: `src/NetEvolve.Pulse.Extensibility/Outbox/IOutboxRepository.cs:59-69,113-124,142-153`; `src/NetEvolve.Pulse.PostgreSql/Outbox/PostgreSqlOutboxRepository.cs:243-265`; `src/NetEvolve.Pulse.SqlServer/Outbox/SqlServerOutboxRepository.cs:273-284`.
- Why it matters: `ProcessBatchSendAsync` (Outbox/OutboxProcessorHostedService.cs:438-439) needs atomic `MarkAsCompletedAsync(ids[])` for at-least-once.
- Test idea: Flaky connection failing after N successes; call `MarkAsCompletedAsync({id1,id2,id3})` failing after id1; assert all-or-none semantics (current: only id1 Completed).

### Q10 — Timeout-cancel vs external-cancel confusion in ProcessMessageAsync
- Claim: When `timeoutCts.CancelAfter` fires, `OperationCanceledException` does not satisfy `when (cancellationToken.IsCancellationRequested)` filter — falls into generic catch and gets treated as retryable failure with msg "The operation was canceled." Telemetry status remains `Ok` while DB state is Failed/DeadLetter.
- Evidence: `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:331-401`; `src/NetEvolve.Pulse/Interceptors/ActivityAndMetricsRequestInterceptor.cs:105-143`.
- Why it matters: "Processed-but-failed" rows with success logs → operator confusion.
- Test idea: `ProcessingTimeout=50ms`, transport sleeps 200ms; assert row Status=Failed, RetryCount=1, `pulse.outbox.failed.total` advanced, and `ExecuteAsync` did not fault.

### Q11 — Race in batch failure classification reads EventTypeOverrides three times
- Claim: `ProcessBatchSendAsync` invokes `_options.GetEffectiveMaxRetryCount(m.EventType)` independently in three LINQ predicates — `EventTypeOverrides` is a public `ConcurrentDictionary` and can mutate between reads, causing dead-letter log lines to disagree with mark-as-failed DB writes.
- Evidence: `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:465-518`; `src/NetEvolve.Pulse/Outbox/OutboxProcessorOptions.cs:118-119`.
- Why it matters: Inconsistent telemetry vs DB state when overrides tuned at runtime.
- Test idea: Custom repo whose `MarkAsFailedAsync(ids[])` delays 50ms; another thread mutates `EventTypeOverrides[typeof(MyEvent)].MaxRetryCount=1`; assert dead-letter log count ≠ DB dead-letter row count.

### Q12 (lower confidence) — Interceptor pipeline ordering is implicit
- Claim: Pipeline ordering is registration-order driven via `Reverse().ToArray()` + wrap-from-innermost-out. No priority hook; users cannot predict whether timeout wraps idempotency or vice versa without inspecting registration order.
- Evidence: `src/NetEvolve.Pulse/Internals/PulseMediator.cs:232,243-249`; `src/NetEvolve.Pulse/IdempotencyExtensions.cs:61-63`; `src/NetEvolve.Pulse/ConcurrentCommandGuardExtensions.cs:50-52`.
- Why it matters: Determines whether timeout fires inside or outside concurrency guard, and whether idempotency conflicts are caught by Polly retry. Fragile, load-bearing on registration order.
- Test idea: Register `AddTimeout()` then `AddIdempotency()` and verify which exception escapes; swap order and observe change.
