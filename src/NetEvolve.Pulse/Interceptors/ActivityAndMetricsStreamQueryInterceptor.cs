namespace NetEvolve.Pulse.Interceptors;

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using static Internals.Defaults.Tags;

/// <summary>
/// Internal interceptor that adds OpenTelemetry activity tracing and metrics collection for all stream queries.
/// This interceptor captures stream query execution time, counts, and error rates with contextual tags.
/// Activities are compatible with distributed tracing systems, and metrics follow Prometheus naming conventions.
/// </summary>
/// <typeparam name="TQuery">The type of stream query being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the stream query.</typeparam>
internal sealed class ActivityAndMetricsStreamQueryInterceptor<TQuery, TResponse>
    : IStreamQueryInterceptor<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    /// <summary>
    /// Counter tracking the total number of stream queries processed, tagged by query type.
    /// </summary>
    private static readonly Counter<long> StreamQueryCounter = Defaults.Meter.CreateCounter<long>(
        "pulse.stream_query.total",
        "queries",
        "Total number of stream queries processed."
    );

    /// <summary>
    /// Counter tracking the total number of stream query errors, tagged by query type.
    /// </summary>
    private static readonly Counter<long> ErrorsCounter = Defaults.Meter.CreateCounter<long>(
        "pulse.stream_query.errors",
        "errors",
        "Total number of stream query errors."
    );

    /// <summary>
    /// Histogram measuring stream query processing duration in milliseconds, with percentile distributions.
    /// </summary>
    private static readonly Histogram<double> StreamQueryDurationHistogram = Defaults.Meter.CreateHistogram<double>(
        "pulse.stream_query.duration",
        "ms",
        "Duration of stream query processing in milliseconds."
    );

    /// <summary>
    /// Cached query name derived from the generic type parameter.
    /// Static fields in generic types are per type instantiation, so this is computed once per <typeparamref name="TQuery"/>.
    /// </summary>
    private static readonly string QueryName = typeof(TQuery).Name;

    /// <summary>
    /// Cached response type name derived from the generic type parameter.
    /// Static fields in generic types are per type instantiation, so this is computed once per <typeparamref name="TResponse"/>.
    /// </summary>
    private static readonly string ResponseTypeName = typeof(TResponse).Name;

    /// <summary>
    /// Time provider for consistent timestamp generation, supporting testability.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityAndMetricsStreamQueryInterceptor{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for timestamp generation.</param>
    public ActivityAndMetricsStreamQueryInterceptor(TimeProvider timeProvider) => _timeProvider = timeProvider;

    /// <inheritdoc />
    /// <remarks>
    /// This method wraps stream query execution with comprehensive telemetry:
    /// <list type="bullet">
    /// <item>Creates an OpenTelemetry activity for distributed tracing</item>
    /// <item>Tags the activity with query name, request type, and response type</item>
    /// <item>Records request and response timestamps on the activity</item>
    /// <item>Increments stream query counter metrics</item>
    /// <item>Measures and records execution duration</item>
    /// <item>Captures exception details on failure</item>
    /// <item>Marks success/failure status in both activity and metrics</item>
    /// <item>Yields items unchanged without buffering</item>
    /// </list>
    /// </remarks>
    public async IAsyncEnumerable<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        const string requestType = "StreamQuery";

        var tags = new TagList
        {
            { RequestType, requestType },
            { RequestName, QueryName },
            { ResponseType, ResponseTypeName },
        };

        using var activity = Defaults.ActivitySource.StartActivity(
            $"StreamQuery.{QueryName}",
            ActivityKind.Internal,
            parentId: null,
            tags: tags
        );

        var startTime = _timeProvider.GetUtcNow();

        _ = activity
            ?.SetStartTime(startTime.UtcDateTime)
            .SetTag(RequestCorrelationId, request.CorrelationId)
            .SetTag(RequestTimestamp, startTime);
        StreamQueryCounter.Add(1, tags);

        // yield return is not allowed inside a try/catch block, so we capture any exception
        // from the inner enumerator and re-throw it after the yield loop completes.
        ExceptionDispatchInfo? caughtExceptionInfo = null;

        var enumerator = handler(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    caughtExceptionInfo = ExceptionDispatchInfo.Capture(ex);
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                // yield return is valid here: it is inside try/finally but NOT inside try/catch
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        if (caughtExceptionInfo is not null)
        {
            var ex = caughtExceptionInfo.SourceException;
            var errorTime = _timeProvider.GetUtcNow();

            // Capture comprehensive exception details in the activity
            _ = activity
                ?.SetStatus(ActivityStatusCode.Error, ex.Message)
                .SetEndTime(errorTime.UtcDateTime)
                .SetTag(ExceptionType, ex.GetType().FullName)
                .SetTag(ExceptionMessage, ex.Message)
                .SetTag(ExceptionStackTrace, ex.StackTrace)
                .SetTag(ExceptionTimestamp, errorTime)
                .SetTag(Success, value: false);

            // Increment error counters and record failed execution duration
            ErrorsCounter.Add(1, tags);
            StreamQueryDurationHistogram.Record(
                (errorTime - startTime).TotalMilliseconds,
                [.. tags, new(Success, false)]
            );

            caughtExceptionInfo.Throw();
        }
        else
        {
            var endTime = _timeProvider.GetUtcNow();

            // Mark activity as successful
            _ = activity
                ?.SetStatus(ActivityStatusCode.Ok)
                .SetEndTime(endTime.UtcDateTime)
                .SetTag(ResponseTimestamp, endTime)
                .SetTag(Success, value: true);

            // Record successful execution duration
            StreamQueryDurationHistogram.Record((endTime - startTime).TotalMilliseconds, [.. tags, new(Success, true)]);
        }
    }
}
