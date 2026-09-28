namespace NetEvolve.Pulse.Interceptors;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
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
    /// Counter <c>pulse.requests.total</c>, tagged by type.
    /// </summary>
    private readonly Counter<long> _requestCounter;

    /// <summary>
    /// Counter <c>pulse.request.errors</c>, tagged by type.
    /// </summary>
    private readonly Counter<long> _errorsCounter;

    /// <summary>
    /// Histogram <c>pulse.request.duration</c> in milliseconds, or in seconds when semantic convention units are enabled.
    /// </summary>
    private readonly Histogram<double> _requestDurationHistogram;

    /// <summary>
    /// Whether the metrics use the units of the OpenTelemetry semantic conventions.
    /// </summary>
    private readonly bool _useSemanticConventionUnits;

    /// <summary>
    /// Time provider for consistent timestamp generation, supporting testability.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityAndMetricsRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for timestamp generation.</param>
    /// <param name="options">The telemetry options; <see langword="null"/> keeps the legacy units.</param>
    public ActivityAndMetricsRequestInterceptor(
        TimeProvider timeProvider,
        IOptions<ActivityAndMetricsOptions>? options = null
    )
    {
        _timeProvider = timeProvider;
        _useSemanticConventionUnits = options?.Value.UseSemanticConventionUnits ?? false;
        _requestCounter = TelemetryUnits.CreateCounter(
            Defaults.Meter,
            "pulse.requests.total",
            "requests",
            "{request}",
            "Total number of requests processed.",
            _useSemanticConventionUnits
        );
        _errorsCounter = TelemetryUnits.CreateCounter(
            Defaults.Meter,
            "pulse.request.errors",
            "errors",
            "{error}",
            "Total number of request errors.",
            _useSemanticConventionUnits
        );
        _requestDurationHistogram = TelemetryUnits.CreateDurationHistogram(
            Defaults.Meter,
            "pulse.request.duration",
            "request processing",
            _useSemanticConventionUnits
        );
    }

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
    /// <item>Leaves the activity status <see cref="ActivityStatusCode.Unset"/> unless the handler fails</item>
    /// <item>Sets <c>error.type</c> on the activity, the error counter and the duration histogram on failure</item>
    /// </list>
    /// </remarks>
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        using var activity = Defaults.ActivitySource.StartActivity(
            $"{requestType}.{requestName}",
            ActivityKind.Internal,
            parentId: null,
            tags: tags
        );

        var startTime = _timeProvider.GetUtcNow();

        _ = activity
            ?.SetStartTime(startTime.UtcDateTime)
            .SetTag(RequestCorrelationId, request.CorrelationId)
            .SetTag(RequestCausationId, request.CausationId)
            .SetTag(RequestTimestamp, startTime);
        _requestCounter.Add(1, tags);

        try
        {
            // Execute the actual request handler
            var response = await handler(request, cancellationToken).ConfigureAwait(false);

            var endTime = _timeProvider.GetUtcNow();

            // Status stays Unset on success, as required by the OpenTelemetry Trace API.
            _ = activity
                ?.SetEndTime(endTime.UtcDateTime)
                .SetTag(ResponseTimestamp, endTime)
                .SetTag(Success, value: true);

            // Record successful execution duration
            _requestDurationHistogram.Record(
                TelemetryUnits.ToDuration(endTime - startTime, _useSemanticConventionUnits),
                [.. tags, new(Success, true)]
            );

            return response;
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
            _requestDurationHistogram.Record(
                TelemetryUnits.ToDuration(errorTime - startTime, _useSemanticConventionUnits),
                [.. tags, new(Success, false), new(ErrorType, errorType)]
            );

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
