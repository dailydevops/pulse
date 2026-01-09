namespace NetEvolve.Pulse.Internals;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using NetEvolve.Pulse.Extensibility;

internal sealed class ActivityAndMetricsInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ActivitySource _activitySource = new ActivitySource("NetEvolve.Pulse", Defaults.Version);

    private static readonly Meter _meter = new Meter("NetEvolve.Pulse", Defaults.Version);

    private static readonly Counter<long> _requestCounter = _meter.CreateCounter<long>(
        "pulse.requests.total",
        "requests",
        "Total number of requests processed."
    );

    private static readonly Counter<long> _errorsCounter = _meter.CreateCounter<long>(
        "pulse.request.errors",
        "errors",
        "Total number of request errors."
    );

    private static readonly Histogram<double> _requestDurationHistogram = _meter.CreateHistogram<double>(
        "pulse.request.duration",
        "ms",
        "Duration of request processing in milliseconds."
    );

    private readonly TimeProvider _timeProvider;

    private const string PulseExceptionMessage = "pulse.exception.message";
    private const string PulseExceptionStackTrace = "pulse.exception.stacktrace";
    private const string PulseExceptionTimestamp = "pulse.exception.timestamp";
    private const string PulseExceptionType = "pulse.exception.type";
    private const string PulseRequestName = "pulse.request.name";
    private const string PulseRequestTimestamp = "pulse.request.timestamp";
    private const string PulseRequestType = "pulse.request.type";
    private const string PulseResponseTimestamp = "pulse.response.timestamp";
    private const string PulseResponseType = "pulse.response.type";
    private const string PulseSuccess = "pulse.success";

    internal ActivityAndMetricsInterceptor(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public async Task<TResponse> HandleAsync(TRequest request, Func<TRequest, Task<TResponse>> handler)
    {
        var requestType = GetRequestType(request);
        var requestName = typeof(TRequest).Name;
        var responseType = typeof(TResponse).Name;
        var activityName = $"{requestType}.{requestName}";

        var tags = new TagList
        {
            { PulseRequestType, requestType },
            { PulseRequestName, requestName },
            { PulseResponseType, responseType },
        };

        using var activity = _activitySource.StartActivity(activityName, ActivityKind.Internal, null, tags: tags);

        var startTime = _timeProvider.GetUtcNow();

        // TODO: Corralation ID
        _ = activity
            ?.SetStartTime(startTime.UtcDateTime)
            .SetTag(PulseRequestType, requestType)
            .SetTag(PulseRequestTimestamp, startTime)
            .SetTag(PulseResponseType, responseType);
        _requestCounter.Add(1, tags);

        try
        {
            var response = await handler(request).ConfigureAwait(false);

            var endTime = _timeProvider.GetUtcNow();

            _ = activity
                ?.SetStatus(ActivityStatusCode.Ok)
                .SetEndTime(endTime.UtcDateTime)
                .SetTag(PulseResponseTimestamp, endTime)
                .SetTag(PulseSuccess, true);

            _requestDurationHistogram.Record(
                (startTime - endTime).TotalMilliseconds,
                [.. tags, new(PulseSuccess, true)]
            );

            return response;
        }
        catch (Exception ex)
        {
            var errorTime = _timeProvider.GetUtcNow();

            _ = activity
                ?.SetStatus(ActivityStatusCode.Error, ex.Message)
                .SetEndTime(errorTime.UtcDateTime)
                .SetTag(PulseExceptionType, ex.GetType().FullName)
                .SetTag(PulseExceptionMessage, ex.Message)
                .SetTag(PulseExceptionStackTrace, ex.StackTrace)
                .SetTag(PulseExceptionTimestamp, errorTime)
                .SetTag(PulseSuccess, false);

            _errorsCounter.Add(1, tags);
            _requestDurationHistogram.Record(
                (startTime - errorTime).TotalMilliseconds,
                [.. tags, new(PulseSuccess, false)]
            );

            throw;
        }
    }

    private static string GetRequestType(TRequest request) =>
        request switch
        {
            ICommand<TResponse> => "Command",
            IQuery<TResponse> => "Query",
            _ => "Request",
        };
}
