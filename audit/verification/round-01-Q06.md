# Q06 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse.Kafka/Outbox/KafkaMessageTransport.cs:63-102` — `SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)` declares the token but never passes it to `_producer.Produce` (the sync overload at line 76 does not accept one) nor to `_producer.Flush` at line 94.
- `src/NetEvolve.Pulse.Kafka/Outbox/KafkaMessageTransport.cs:94` — `_ = _producer.Flush(Timeout.InfiniteTimeSpan);` — explicitly waits forever, ignoring the token.
- `src/NetEvolve.Pulse.Kafka/Outbox/KafkaMessageTransport.cs:105-116` — `IsHealthyAsync` calls sync `_adminClient.GetMetadata(TimeSpan.FromSeconds(5))` and only checks the token in the catch filter; it does not pre-check `cancellationToken.ThrowIfCancellationRequested()` either.
- The `IProducer<TKey, TValue>` interface offers `Flush(CancellationToken)` (see `Confluent.Kafka` SDK). The token-aware overload is `_producer.Flush(cancellationToken)` and *will* throw `OperationCanceledException` when the supplied token fires.

**Reasoning:**
The Kafka transport ignores cancellation in the two places it matters: the synchronous batch flush and the sync admin-client health probe. During graceful shutdown (`IHostApplicationLifetime.StopApplication`), the hosted service signals `stoppingToken` and expects all transports to wind down. With `Flush(Timeout.InfiniteTimeSpan)` the transport blocks forever if the broker is unreachable. The stuck flush keeps the row in `Status=Processing` (compounding Q02). The fix is one line: `_producer.Flush(cancellationToken)`.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Kafka/KafkaMessageTransportCancellationTests.cs`
- Status: written

The test plugs a fake `IProducer<string, string>` whose `Flush(TimeSpan)` blocks indefinitely on a `ManualResetEventSlim` while ignoring its argument, and whose `Flush(CancellationToken)` honors the token (the real Confluent producer behaves this way). It then calls `SendBatchAsync` with a `CancellationTokenSource` set to cancel after 100ms. The task must complete (cancelled or successful) within 1 second. Today it hangs because the transport calls the timespan overload with `Timeout.InfiniteTimeSpan`, so the assertion-with-timeout fails.

**Notes:**
The fix is local to `KafkaMessageTransport.SendBatchAsync` (line 94) and `IsHealthyAsync` (line 109). For `IsHealthyAsync`, the right pattern is `Task.Run(() => _adminClient.GetMetadata(timeout), cancellationToken)` or use the admin client's cancellation-token overload if available; alternatively `cancellationToken.ThrowIfCancellationRequested()` before the call so the pre-cancelled path is fast. Document this transport as best-effort for `IsHealthy` and note the sync API limitation in librdkafka.
