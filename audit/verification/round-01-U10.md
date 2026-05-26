# U10 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse.Kafka/Outbox/KafkaMessageTransport.cs:94` — `_ = _producer.Flush(Timeout.InfiniteTimeSpan);` inside `SendBatchAsync`. The `CancellationToken` parameter (line 63) is **never observed** in the method body; no `ThrowIfCancellationRequested`, no token-bound overload (`Flush(CancellationToken)` exists on `IProducer` but is not used).
- `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:432-435` — caller wraps `SendBatchAsync` with `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` and `CancelAfter(_options.ProcessingTimeout)`. The token cancellation never reaches the underlying broker call because `Flush(TimeSpan)` is infinite and ignores the token.

**Reasoning:** A stuck broker — or any unreachable broker that the Confluent.Kafka client is still trying to flush — leaves the processor thread parked indefinitely. `ProcessingTimeout` is documented as the per-batch bound (XML doc at `OutboxProcessorHostedService.cs:421-423`) but cannot enforce that bound because `SendBatchAsync` does not propagate cancellation to `Flush`. The fix is mechanical: either call `_producer.Flush(cancellationToken)` (token overload exists on `IProducer<TKey,TValue>`), or `Flush` with a bounded `TimeSpan` and check the token in a loop, throwing `OperationCanceledException` on cancellation. The existing `KafkaMessageTransportTests` already include a `FakeProducer` with a `Flush(TimeSpan)` and `Flush(CancellationToken)` pair, making the fake easy to extend.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Kafka/KafkaMessageTransportCancellationTests.cs`
- Status: written

```csharp
namespace NetEvolve.Pulse.Tests.Unit.Kafka;

// (See test file for full source.)
```

The test:

1. Builds a `BlockingFlushProducer` whose `Flush(TimeSpan)` blocks indefinitely on a `ManualResetEventSlim` (mirrors a stuck broker).
2. Constructs a `KafkaMessageTransport` with it.
3. Creates a `CancellationTokenSource`, schedules cancellation after 100 ms.
4. Calls `SendBatchAsync(messages, cts.Token)` and asserts that the returned task **completes** (faulted or cancelled is fine) within 200 ms.

Today the `SendBatchAsync` task never completes — the test waits the full assertion window (`WaitAsync(200ms)`) and the `Throws<TimeoutException>` assertion succeeds (proving the bug). Phase 3 fix flips the cancellation behavior; the assertion is updated to `IsCompletedSuccessfullyOrCancelled` after the fix.

**Notes:**
- The test uses `Task.WaitAsync(TimeSpan)` to enforce the 200 ms ceiling without relying on the transport's own token observation — that's exactly the path the bug breaks.
- The fake `Produce` no-ops so message-enqueue does not block; only `Flush` blocks. This isolates U10 to the documented call site (`KafkaMessageTransport.cs:94`).
- After the Phase 3 fix, the test's expectation flips: replace the `TimeoutException` assertion with an `OperationCanceledException`/`TaskCanceledException` assertion (or assert the returned task is cancelled). Leave a `// TODO: U10 fix landed — flip assertion` comment in the test until the fix lands.
- A second pre-existing test (`SendBatchAsync_Enqueues_all_messages_and_flushes`) is unaffected — the new fake is local to this test file.
