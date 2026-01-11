namespace NetEvolve.Pulse.Interceptors;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using static Internals.Defaults.Tags;

/// <summary>
/// Internal interceptor that adds OpenTelemetry activity tracing and metrics collection for all requests.
/// This interceptor captures request execution time, counts, and error rates with rich contextual tags.
/// Activities are compatible with distributed tracing systems, and metrics follow Prometheus naming conventions.
/// </summary>
/// <typeparam name="TRequest">The type of request being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
internal sealed class ActivityAndMetricsRequestInterceptor<TRequest, TResponse>
    : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
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
    /// Counter tracking the total number of requests processed, tagged by request type.
    /// </summary>
    private static readonly Counter<long> _requestCounter = _meter.CreateCounter<long>(
        "pulse.requests.total",
        "requests",
        "Total number of requests processed."
    );

    /// <summary>
    /// Counter tracking the total number of request errors, tagged by request type.
    /// </summary>
    private static readonly Counter<long> _errorsCounter = _meter.CreateCounter<long>(
        "pulse.request.errors",
        "errors",
        "Total number of request errors."
    );

    /// <summary>
    /// Histogram measuring request processing duration in milliseconds, with percentile distributions.
    /// </summary>
    private static readonly Histogram<double> _requestDurationHistogram = _meter.CreateHistogram<double>(
        "pulse.request.duration",
        "ms",
        "Duration of request processing in milliseconds."
    );

    /// <summary>
    /// Time provider for consistent timestamp generation, supporting testability.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityAndMetricsRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for timestamp generation.</param>
    public ActivityAndMetricsRequestInterceptor(TimeProvider timeProvider) => _timeProvider = timeProvider;

    /// <inheritdoc />
    /// <remarks>
    /// This method wraps request execution with comprehensive telemetry:
    /// <list type="bullet">
    /// <item>Creates an OpenTelemetry activity for distributed tracing</item>
    /// <item>Tags the activity with request type, name, and response type</item>
    /// <item>Increments request counter metrics</item>
    /// <item>Measures and records execution duration</item>
    /// <item>Captures exception details on failure</item>
    /// <item>Marks success/failure status in both activity and metrics</item>
    /// </list>
    /// </remarks>
    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, Task<TResponse>> handler)
    {
        // Determine request categorization (Command/Query/Request)
        var requestType = GetRequestType(request);
        var requestName = typeof(TRequest).Name;
        var responseType = typeof(TResponse).Name;

        // Prepare tags for consistent labeling across activity and metrics
        var tags = new TagList
        {
            { RequestType, requestType },
            { RequestName, requestName },
            { ResponseType, responseType },
        };

        using var activity = _activitySource.StartActivity(
            $"{requestType}.{requestName}",
            ActivityKind.Internal,
            null,
            tags: tags
        );

        var startTime = _timeProvider.GetUtcNow();

        _ = activity
            ?.SetStartTime(startTime.UtcDateTime)
            .SetTag(RequestCorrelationId, request.CorrelationId)
            .SetTag(RequestTimestamp, startTime);
        _requestCounter.Add(1, tags);

        try
        {
            // Execute the actual request handler
            var response = await handler(request).ConfigureAwait(false);

            var endTime = _timeProvider.GetUtcNow();

            // Mark activity as successful
            _ = activity
                ?.SetStatus(ActivityStatusCode.Ok)
                .SetEndTime(endTime.UtcDateTime)
                .SetTag(ResponseTimestamp, endTime)
                .SetTag(Success, true);

            // Record successful execution duration
            _requestDurationHistogram.Record((startTime - endTime).TotalMilliseconds, [.. tags, new(Success, true)]);

            return response;
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
            _requestDurationHistogram.Record((startTime - errorTime).TotalMilliseconds, [.. tags, new(Success, false)]);

            throw;
        }
    }

    /// <summary>
    /// Determines the semantic type of the request based on its interface implementation.
    /// Commands represent state-changing operations, queries are read-only, and generic requests are fallback.
    /// </summary>
    /// <param name="request">The request to categorize.</param>
    /// <returns>A string indicating "Command", "Query", or "Request".</returns>
    private static string GetRequestType(TRequest request) =>
        request switch
        {
            ICommand<TResponse> => "Command",
            IQuery<TResponse> => "Query",
            _ => "Request",
        };
}
