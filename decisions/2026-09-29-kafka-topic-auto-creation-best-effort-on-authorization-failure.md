---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.Kafka/**/*.cs"
  - "src/NetEvolve.Pulse.Kafka/README.md"

created: 2026-09-29

lastModified: 2026-09-29

state: proposed

instructions: |
  KafkaMessageTransport topic auto-creation is best effort when the principal is not allowed to create topics.
  When CreateTopicsAsync reports TopicAuthorizationFailed or ClusterAuthorizationFailed, the transport MUST cache the topic, MUST NOT call CreateTopicsAsync for it again, and MUST let the produce call decide. It probes GetMetadata(topic) once and logs a warning only when existence cannot be confirmed.
  Any other CreateTopics error MUST fail the send with an InvalidOperationException that names the topic and KafkaTransportOptions.AutoCreateTopics, and MUST NOT be cached.
  SendBatchAsync MUST still flush messages already enqueued when topic creation fails for a later message, whatever the exception (CreateTopics error, request-level KafkaException or cancellation), and MUST report that failure in its AggregateException. Within one batch it MUST NOT retry creation for a topic that already failed.
  The GetMetadata probe MUST run off the calling thread and honor the caller's cancellation token.
---

# Decision: Kafka Topic Auto-Creation Is Best Effort on Authorization Failure

`NetEvolve.Pulse.Kafka` keeps `AutoCreateTopics = true` as the default. A missing `CREATE` permission no longer blocks delivery: the transport stops trying to create the topic and lets the producer write to it.

## Context

Before every send, `KafkaMessageTransport.EnsureTopicAsync` called `IAdminClient.CreateTopicsAsync` and only tolerated `TopicAlreadyExists`. Managed clusters (for example Confluent Cloud service accounts) often grant `WRITE` and `DESCRIBE` on pre-provisioned topics but not `CREATE`. The broker checks authorization before existence (`ControllerApis.createTopics` answers unauthorized names with `TOPIC_AUTHORIZATION_FAILED` and removes them before the existence check), so an existing topic still fails. Nothing was cached, so every send failed and every outbox message ended up dead-lettered (issue #845). Kafka Connect had the same problem ([KAFKA-6250](https://issues.apache.org/jira/browse/KAFKA-6250)).

The [Kafka protocol error table](https://kafka.apache.org/43/design/protocol) lists `TOPIC_AUTHORIZATION_FAILED` (29, "Topic authorization failed.") and `CLUSTER_AUTHORIZATION_FAILED` (31, "Cluster authorization failed.") as not retriable. Confluent.Kafka's [`CreateTopicsException`](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.Admin.CreateTopicsException.html) carries "the result corresponding to all topics in the request", one `CreateTopicReport` per topic with its own `Error.Code`.

## Decision

- `TopicAlreadyExists`: unchanged, the topic is cached.
- `TopicAuthorizationFailed` or `ClusterAuthorizationFailed`: the transport calls `IAdminClient.GetMetadata(topic, 5s)` once, offloaded with `Task.Run(...).WaitAsync(cancellationToken)` like `IsHealthyAsync`. The topic is cached either way. When the metadata does not show the topic without error, or the call throws a `KafkaException`, a warning is logged that names the topic and `AutoCreateTopics`. The produce call surfaces the real error if the topic is missing or not writable.
- Any other error code: the send fails with an `InvalidOperationException` that names the topic and `AutoCreateTopics` and wraps the original `CreateTopicsException`. The topic is not cached, so the next send tries again.
- `SendBatchAsync` collects any topic-creation failure for a message like a produce failure, still flushes all enqueued messages and throws one `AggregateException` at the end. A topic that failed is not tried again in the same batch; its later messages get the same exception. A cancellation is observed again by the final `Flush(cancellationToken)`, which throws `OperationCanceledException`.
- The transport takes an optional `ILogger<KafkaMessageTransport>`; without one it logs nothing.

## Consequences

- Producers without `CREATE` deliver to pre-provisioned topics with default options. They see one warning per topic and process when the topic cannot be described; setting `AutoCreateTopics = false` avoids both the create call and the warning.
- If the topic really is missing, the failure moves from `CreateTopics` to the produce call (`UNKNOWN_TOPIC_OR_PART` or a produce timeout). The outbox retry and dead-letter path is unchanged.
- One extra metadata round trip per topic and process, only after an authorization failure.
- A batch whose topic creation fails for one message no longer strands earlier messages without a flush.

## Alternatives Considered

- **Default `AutoCreateTopics` to `false`.** Rejected. It changes behavior for every existing user that relies on auto-creation; the issue only asks to document the switch.
- **Cache every failure, not only authorization failures.** Rejected. Errors such as `PolicyViolation` or `InvalidReplicationFactor` point at a configuration the user must fix, and producing to a topic that was never created would only move the error.
- **Skip the metadata probe and always warn.** Rejected. Users with `DESCRIBE` on pre-provisioned topics would get a warning for a setup that works.

## Related Decisions

- [Extensibility Interface Evolution Before 1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - `IMessageTransport` is unchanged; only the Kafka implementation and its constructor change.
