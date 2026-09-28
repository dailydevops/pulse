---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.RabbitMQ/**/*.cs"
  - "src/NetEvolve.Pulse.RabbitMQ/README.md"

created: 2026-09-28

lastModified: 2026-09-28

state: proposed

instructions: |
  RabbitMqConnectionAdapter MUST create every channel with new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), built per call.
  RabbitMqMessageTransport MUST publish with Persistent = true (delivery mode 2) and mandatory: true, so SendAsync/SendBatchAsync only complete after the broker ack and throw on nack, basic.return or channel close.
  SendBatchAsync MUST start all publishes of a batch on its single rented channel before awaiting them, and MUST return the channel only after every publish has settled.
  MUST NOT add options to turn confirms, persistence or mandatory routing off without superseding this decision.
---

# Decision: RabbitMQ Transport Always Publishes With Publisher Confirms, Persistent Delivery and Mandatory Routing

`NetEvolve.Pulse.RabbitMQ` treats a publish as successful only after the broker has confirmed it. Confirms, persistent delivery mode and mandatory routing are always on and cannot be turned off.

## Context

`RabbitMqMessageTransport` returned once the frame was written to the socket. The outbox processor then marked the message `Completed`, even when the exchange was missing, no binding matched the routing key, or the broker restarted with transient messages queued (issue #818). This breaks the at-least-once contract of `IMessageTransport`.

[RabbitMQ: Publisher Confirms](https://www.rabbitmq.com/docs/confirms) says a client that has written a frame to its socket "cannot assume that the message has reached the server". Unroutable messages are still acked unless `mandatory` is set, in which case "the `basic.return` is sent to the client before `basic.ack`". [Queues, Durability](https://www.rabbitmq.com/docs/queues#durability) says transient messages "will be discarded during recovery, even if they were stored in durable queues".

In RabbitMQ.Client 7.x, `CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true)` makes `BasicPublishAsync` wait for the broker's confirm. Per the client's XML documentation, it "throws `PublishException` if a nack or basic.return is returned for the message".

## Decision

- Every pooled channel is created with publisher confirmations and confirmation tracking enabled. No outstanding-confirms rate limiter is applied (`outstandingPublisherConfirmationsRateLimiter: null`, which is also what the public `CreateChannelOptions` constructor defaults to). A batch therefore has at most `OutboxProcessorOptions.BatchSize` publishes in flight on its channel, and is never rejected or throttled by the client's `ThrottlingRateLimiter`.
- Every message is published with `Persistent = true` (delivery mode 2) and `mandatory: true`.
- `SendBatchAsync` keeps one rented channel per batch. It starts every publish of the batch before awaiting any of them, following the client's `PublishMessagesInBatchAsync` sample. It rethrows the first failure only after all publishes have settled, and only then returns the channel to the pool.
- A channel closed by the broker (for example after `404 NOT_FOUND` for a missing exchange) is discarded by `RabbitMqChannelPool.Return`, which already checks `IsOpen`.
- No options are added to opt out.

## Consequences

- Outbox messages are marked `Completed` only after the broker has accepted, routed and, for durable queues, persisted them. Failures go through the existing outbox retry and dead-letter path.
- Each `SendAsync` waits one broker round trip for the confirm. Batches overlap their confirms, so the added latency is paid once per batch rather than once per message.
- A message whose routing key matches no binding now fails and retries, and is dead-lettered after the retry limit instead of being dropped silently. Deployments that publish event types nobody subscribes to must add a binding, for example to a catch-all queue or through an alternate exchange.
- A failed batch is retried as a whole. `SendBatchAsync` publishes every message of the batch even after one fails, so every message the broker already confirmed is delivered again on each retry, up to the retry limit. With mandatory routing, one unroutable message in a batch is the typical trigger. Consumers must be idempotent; `MessageId` carries the outbox message id for de-duplication.
- The client adds the `x-dotnet-pub-seq-no` header (`Constants.PublishSequenceNumberHeader`) to every message. Consumers can see it.

## Alternatives Considered

- **Options on `RabbitMqTransportOptions` for confirms, persistence and mandatory routing.** Rejected for now. Turning any of them off brings back silent loss, and no use case needs it yet. Adding one later is non-breaking.
- **Confirms without tracking, correlating via `GetNextPublishSequenceNumberAsync`.** Rejected. The client docs say a `basic.return` without tracking "will not, however, contain the publish sequence number", so it cannot be tied to the message.
- **Publishing a batch sequentially and awaiting each confirm.** Rejected. It costs one round trip per message.

## Related Decisions

- [Extensibility Interface Evolution Before 1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - `IMessageTransport` is unchanged; only the RabbitMQ implementation changes.
