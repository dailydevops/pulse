namespace NetEvolve.Pulse.Internals;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using NetEvolve.Pulse.Extensibility;
using static Defaults.Tags;

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
    /// The activity source for creating distributed tracing activities.
    /// </summary>
    private static readonly ActivitySource _activitySource = new ActivitySource("NetEvolve.Pulse", Defaults.Version);

    /// <summary>
    /// The meter for creating metrics instruments.
    /// </summary>
    private static readonly Meter _meter = new Meter("NetEvolve.Pulse", Defaults.Version);

    /// <summary>
    /// Counter tracking the total number of events processed, tagged by event type.
    /// </summary>
    private static readonly Counter<long> _eventCounter = _meter.CreateCounter<long>(
        "pulse.events.total",
        "events",
        "Total number of events processed."
    );

    /// <summary>
    /// Counter tracking the total number of event errors, tagged by event type.
    /// </summary>
    private static readonly Counter<long> _errorsCounter = _meter.CreateCounter<long>(
        "pulse.event.errors",
        "errors",
        "Total number of event errors."
    );

    /// <summary>
    /// Histogram measuring event processing duration in milliseconds, with percentile distributions.
    /// </summary>
    private static readonly Histogram<double> _eventDurationHistogram = _meter.CreateHistogram<double>(
        "pulse.event.duration",
        "ms",
        "Duration of event processing in milliseconds."
    );

    /// <summary>
    /// Time provider for consistent timestamp generation, supporting testability.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityAndMetricsEventInterceptor{TEvent}"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for timestamp generation.</param>
    internal ActivityAndMetricsEventInterceptor(TimeProvider timeProvider) => _timeProvider = timeProvider;

    /// <inheritdoc />
    /// <remarks>
    /// This method wraps event execution with comprehensive telemetry:
    /// <list type="bullet">
    /// <item>Creates an OpenTelemetry activity for distributed tracing</item>
    /// <item>Tags the activity with event type and name</item>
    /// <item>Increments event counter metrics</item>
    /// <item>Measures and records execution duration</item>
    /// <item>Captures exception details on failure</item>
    /// <item>Marks success/failure status in both activity and metrics</item>
    /// </list>
    /// </remarks>
    public async Task HandleAsync(TEvent message, Func<TEvent, Task> handler)
    {
        var eventType = "Event";
        var eventName = typeof(TEvent).Name;

        // Prepare tags for consistent labeling across activity and metrics
        var tags = new TagList { { EventType, eventType }, { EventName, eventName } };

        using var activity = _activitySource.StartActivity(
            $"{eventType}.{eventName}",
            ActivityKind.Internal,
            null,
            tags: tags
        );

        var startTime = _timeProvider.GetUtcNow();

        _ = activity?.SetStartTime(startTime.UtcDateTime)
            .SetTag(EventCorrelationId, message.CorrelationId).SetTag(EventTimestamp, startTime);
        _eventCounter.Add(1, tags);

        try
        {
            // Execute the actual event handler
            await handler(message).ConfigureAwait(false);

            var endTime = _timeProvider.GetUtcNow();

            // Mark activity as successful
            _ = activity
                ?.SetStatus(ActivityStatusCode.Ok)
                .SetEndTime(endTime.UtcDateTime)
                .SetTag(EventCompletionTimestamp, endTime)
                .SetTag(Success, true);

            // Record successful execution duration
            _eventDurationHistogram.Record((endTime - startTime).TotalMilliseconds, [.. tags, new(Success, true)]);
        }
        catch (Exception ex)
        {
            var errorTime = _timeProvider.GetUtcNow();

            // Capture comprehensive exception details in the activity
            _ = activity
                ?.SetStatus(ActivityStatusCode.Error, ex.Message)
                .SetEndTime(errorTime.UtcDateTime)
                .SetTag(ExceptionType, ex.GetType().FullName)
                .SetTag(ExceptionMessage, ex.Message)
                .SetTag(ExceptionStackTrace, ex.StackTrace)
                .SetTag(ExceptionTimestamp, errorTime)
                .SetTag(Success, false);

            // Increment error counters and record failed execution duration
            _errorsCounter.Add(1, tags);
            _eventDurationHistogram.Record((errorTime - startTime).TotalMilliseconds, [.. tags, new(Success, false)]);

            throw;
        }
    }
}
