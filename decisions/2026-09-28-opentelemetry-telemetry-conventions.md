---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse/Interceptors/ActivityAndMetrics*.cs"
  - "src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs"
  - "src/NetEvolve.Pulse/Internals/Defaults.cs"
  - "src/NetEvolve.Pulse/Internals/OperationInstruments.cs"

created: 2026-09-28

lastModified: 2026-09-28

state: proposed

instructions: |
  MUST leave the activity status Unset when a Pulse operation succeeds or a stream consumer stops early; MUST set Error with the exception message only on failure.
  MUST set the error.type tag to the exception's full type name on the span, the error counter and the duration histogram of a failed operation, and MUST NOT set it otherwise.
  MUST record the stream query duration once for every outcome (completed, faulted, abandoned) and tag the abandoned outcome with pulse.stream.completed=false.
  MUST keep the legacy units (ms, requests, events, queries, errors, messages) as default until the opt-in ActivityAndMetricsOptions.UseSemanticConventionUnits becomes the default in a later 0.x release; with the opt-in, durations use seconds (unit s) with explicit bucket advice and counters use UCUM annotations ({request}, {event}, {query}, {error}, {message}).
  MUST keep the existing metric and tag names.
---

# Decision: OpenTelemetry Telemetry Conventions

Pulse activities and metrics follow the OpenTelemetry Trace API and semantic conventions for status handling, `error.type` and units. Status and `error.type` changes apply immediately, the unit changes are opt-in during a transition period.

## Context

Issue #832 lists the deviations of the built-in telemetry in `NetEvolve.Pulse` from the OpenTelemetry guidance:

* All three `ActivityAndMetrics*Interceptor` classes set `ActivityStatusCode.Ok` on success. The [Trace API](https://opentelemetry.io/docs/specs/otel/trace/api/#set-status) states: "Generally, Instrumentation Libraries SHOULD NOT set the status code to `Ok`, unless explicitly configured to do so."
* Failed operations carry no `error.type`. [Recording errors](https://opentelemetry.io/docs/specs/semconv/general/recording-errors/) states that spans and metrics SHOULD carry `error.type` on failure and SHOULD NOT include it on success.
* Duration histograms use `ms`, counters use plain words (`requests`, `errors`, `messages`). The [metrics guidelines](https://opentelemetry.io/docs/specs/semconv/general/metrics/#instrument-units) state: "When instruments are measuring durations, seconds (i.e. `s`) SHOULD be used." and ask for UCUM annotations such as `{request}`.

Issue #831 shows that `ActivityAndMetricsStreamQueryInterceptor` records no duration and no outcome when the consumer stops enumerating early, because the recording code runs after the `try`/`finally` block that holds the `yield return`.

No earlier decision defines the Pulse telemetry contract. Units are part of the exported metric identity: the OpenTelemetry Prometheus exporter appends the unit to the metric name (for example `_milliseconds` or `_seconds`). Changing a unit therefore breaks existing dashboards and alerts.

## Decision

* MUST leave the activity status `Unset` when an operation succeeds and when a stream consumer stops early. MUST set `Error` with the exception message on failure.
* MUST set `error.type` to `Exception.GetType().FullName` on the span, the error counter and the duration histogram of a failed operation. The value MUST be identical on all three. The existing `pulse.exception.*` tags stay.
* MUST record `pulse.stream_query.duration` exactly once per stream query, from the `finally` block of the iterator. A stream the consumer abandons carries `pulse.stream.completed=false` on the histogram and the activity, keeps the status `Unset` and carries no `pulse.success` tag. Completed and faulted streams keep their `pulse.success` semantics.
* MUST treat an exception thrown synchronously by the stream handler delegate like an exception thrown during enumeration.
* MUST offer the new units through `ActivityAndMetricsOptions.UseSemanticConventionUnits` (default `false`). The option applies to the interceptors and to `OutboxProcessorHostedService`:

  | Instrument | Legacy unit | Opt-in unit |
  | --- | --- | --- |
  | `pulse.requests.total` | `requests` | `{request}` |
  | `pulse.events.total` | `events` | `{event}` |
  | `pulse.stream_query.total` | `queries` | `{query}` |
  | `pulse.request.errors`, `pulse.event.errors`, `pulse.stream_query.errors` | `errors` | `{error}` |
  | `pulse.outbox.processed.total`, `pulse.outbox.failed.total`, `pulse.outbox.deadletter.total`, `pulse.outbox.pending` | `messages` | `{message}` |
  | `pulse.request.duration`, `pulse.event.duration`, `pulse.stream_query.duration`, `pulse.outbox.processing.duration` | `ms` | `s` |

* MUST provide explicit histogram bucket boundaries for the `s` unit on .NET 9 and later (`InstrumentAdvice<T>`), using the boundaries of the HTTP semantic conventions: 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10.
* MUST keep the metric names and tag names unchanged.
* Transition plan: a later `0.x` release makes `UseSemanticConventionUnits` the default. The legacy units and the option are removed before `1.0.0`.

## Consequences

* Backends treat successful Pulse spans as `Unset` and group failures by `error.type`.
* Abandoned streams appear in the duration histogram, so `pulse.stream_query.total` and `pulse.stream_query.duration` stay consistent.
* Dashboards that filter on `Ok` status must filter on "not `Error`" instead.
* Operators opt into the new units once their dashboards and alerts are migrated. Until then the exported metric names stay as they are.
* A process that mixes both unit modes creates two instruments with the same name on the `NetEvolve.Pulse` meter. The option is meant to be set once per application.

## Alternatives Considered

* **Switch the units immediately.** Rejected: issue #832 requires an opt-in during the transition, because the unit is part of the exported metric name.
* **AppContext switch instead of options.** Rejected: a process-wide switch cannot be tested per test, and the repository already configures interceptors through options.
* **Rename the `.total` counters.** Not part of this decision. Renaming breaks every dashboard without an opt-in path and can be decided separately.

## Related Decisions

* [Extensibility Interface Evolution Before 1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - Governs how the changed telemetry contract is announced while the major version is `0`.
* [DateTimeOffset and TimeProvider Usage](./2026-01-21-datetimeoffset-and-timeprovider-usage.md) - Durations are measured with `TimeProvider`.
