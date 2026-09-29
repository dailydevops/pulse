namespace NetEvolve.Pulse.Interceptors;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using static Internals.Defaults.Tags;

/// <summary>
/// Internal interceptor that adds OpenTelemetry activity tracing and metrics collection for all events.
/// This interceptor captures event execution time, counts, and error rates with rich contextual tags.
/// Activities are compatible with distributed tracing systems, and metrics follow Prometheus naming conventions.
/// </summary>
/// <typeparam name="TEvent">The type of event being intercepted.</typeparam>
internal sealed class ActivityAndMetricsEventInterceptor<TEvent> : IEventInterceptor<TEvent>
    where TEvent : IEvent
{
    /// <summary>
    /// Counter <c>pulse.events.total</c>, tagged by type.
    /// </summary>
    private readonly Counter<long> _eventCounter;

    /// <summary>
    /// Counter <c>pulse.event.errors</c>, tagged by type.
    /// </summary>
    private readonly Counter<long> _errorsCounter;

    /// <summary>
    /// Histogram <c>pulse.event.duration</c> in milliseconds, or in seconds when semantic convention units are enabled.
    /// </summary>
    private readonly Histogram<double> _eventDurationHistogram;

    /// <summary>
    /// Whether the metrics use the units of the OpenTelemetry semantic conventions.
    /// </summary>
    private readonly bool _useSemanticConventionUnits;

    /// <summary>
    /// Time provider for consistent timestamp generation, supporting testability.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityAndMetricsEventInterceptor{TEvent}"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for timestamp generation.</param>
    /// <param name="options">The telemetry options; <see langword="null"/> keeps the legacy units.</param>
    public ActivityAndMetricsEventInterceptor(
        TimeProvider timeProvider,
        IOptions<ActivityAndMetricsOptions>? options = null
    )
    {
        _timeProvider = timeProvider;
        _useSemanticConventionUnits = options?.Value.UseSemanticConventionUnits ?? false;
        _eventCounter = TelemetryUnits.GetSharedCounter(
            "pulse.events.total",
            "events",
            "{event}",
            "Total number of events processed.",
            _useSemanticConventionUnits
        );
        _errorsCounter = TelemetryUnits.GetSharedCounter(
            "pulse.event.errors",
            "errors",
            "{error}",
            "Total number of event errors.",
            _useSemanticConventionUnits
        );
        _eventDurationHistogram = TelemetryUnits.GetSharedDurationHistogram(
            "pulse.event.duration",
            "event processing",
            _useSemanticConventionUnits
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method wraps event execution with comprehensive telemetry:
    /// <list type="bullet">
    /// <item>Creates an OpenTelemetry activity for distributed tracing</item>
    /// <item>Tags the activity with event type and name</item>
    /// <item>Increments event counter metrics</item>
    /// <item>Measures and records execution duration</item>
    /// <item>Captures exception details on failure</item>
    /// <item>Tags <c>pulse.success</c> on the activity and the duration histogram</item>
    /// <item>Leaves the activity status <see cref="ActivityStatusCode.Unset"/> unless the handler fails</item>
    /// <item>Sets <c>error.type</c> on the activity, the error counter and the duration histogram on failure</item>
    /// </list>
    /// </remarks>
    public async Task HandleAsync(
        TEvent message,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string eventType = "Event";
        var eventName = typeof(TEvent).Name;

        // Prepare tags for consistent labeling across activity and metrics
        var tags = new TagList { { EventType, eventType }, { EventName, eventName } };

        using var activity = Defaults.ActivitySource.StartActivity(
            $"{eventType}.{eventName}",
            ActivityKind.Internal,
            parentId: null,
            tags: tags
        );

        var startTime = _timeProvider.GetUtcNow();

        _ = activity
            ?.SetStartTime(startTime.UtcDateTime)
            .SetTag(EventCorrelationId, message.CorrelationId)
            .SetTag(EventCausationId, message.CausationId)
            .SetTag(EventTimestamp, startTime);
        _eventCounter.Add(1, tags);

        try
        {
            // Execute the actual event handler
            await handler(message, cancellationToken).ConfigureAwait(false);

            var endTime = _timeProvider.GetUtcNow();

            // Status stays Unset on success, as required by the OpenTelemetry Trace API.
            _ = activity
                ?.SetEndTime(endTime.UtcDateTime)
                .SetTag(EventCompletionTimestamp, endTime)
                .SetTag(Success, value: true);

            // Record successful execution duration
            _eventDurationHistogram.Record(
                TelemetryUnits.ToDuration(endTime - startTime, _useSemanticConventionUnits),
                [.. tags, new(Success, true)]
            );
        }
        catch (Exception ex)
        {
            var errorTime = _timeProvider.GetUtcNow();
            var errorType = ex.GetType().FullName;

            // Capture comprehensive exception details in the activity
            _ = activity
                ?.SetStatus(ActivityStatusCode.Error, ex.Message)
                .SetEndTime(errorTime.UtcDateTime)
                .SetTag(ErrorType, errorType)
                .SetTag(ExceptionType, errorType)
                .SetTag(ExceptionMessage, ex.Message)
                .SetTag(ExceptionStackTrace, ex.StackTrace)
                .SetTag(ExceptionTimestamp, errorTime)
                .SetTag(Success, value: false);

            // Increment error counters and record failed execution duration
            _errorsCounter.Add(1, [.. tags, new(ErrorType, errorType)]);
            _eventDurationHistogram.Record(
                TelemetryUnits.ToDuration(errorTime - startTime, _useSemanticConventionUnits),
                [.. tags, new(Success, false), new(ErrorType, errorType)]
            );

            throw;
        }
    }
}
