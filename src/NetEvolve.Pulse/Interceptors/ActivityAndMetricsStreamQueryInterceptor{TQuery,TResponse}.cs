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
    /// <item>Leaves the activity status <see cref="ActivityStatusCode.Unset"/> unless the stream faults</item>
    /// <item>Sets <c>error.type</c> on the activity, the error counter and the duration histogram on failure</item>
    /// <item>
    /// Records the duration exactly once for every outcome. A stream whose consumer stops enumerating early
    /// (for example <see langword="break"/>, <c>Take</c> or a disconnected client) is tagged <c>pulse.stream.completed=false</c>
    /// and carries no <c>pulse.success</c> tag
    /// </item>
    /// <item>Yields items unchanged without buffering</item>
    /// </list>
    /// </remarks>
    public async IAsyncEnumerable<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            .SetTag(StreamQueryCausationId, request.CausationId)
            .SetTag(RequestTimestamp, startTime);
        StreamQueryCounter.Add(1, tags);

        // yield return is not allowed inside a try/catch block, so we capture any exception
        // from the handler or the inner enumerator and re-throw it after the finally block ran.
        ExceptionDispatchInfo? caughtExceptionInfo = null;
        var completed = false;
        IAsyncEnumerator<TResponse>? enumerator = null;

        try
        {
            try
            {
                enumerator = handler(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                caughtExceptionInfo = ExceptionDispatchInfo.Capture(ex);
            }

            while (enumerator is not null)
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
                    completed = true;
                    break;
                }

                // yield return is valid here: it is inside try/finally but NOT inside try/catch
                yield return enumerator.Current;
            }
        }
        finally
        {
            try
            {
                if (enumerator is not null)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                // Runs for every outcome, including a consumer that stops early and disposes the iterator.
                RecordOutcome(activity, tags, startTime, caughtExceptionInfo?.SourceException, completed);
            }
        }

        caughtExceptionInfo?.Throw();
    }

    /// <summary>
    /// Records the duration and outcome of a stream query on the activity and the metrics.
    /// </summary>
    /// <param name="activity">The activity of the stream query, if sampled.</param>
    /// <param name="tags">The base tags of the stream query.</param>
    /// <param name="startTime">The time the stream query started.</param>
    /// <param name="exception">The exception that faulted the stream, or <see langword="null"/>.</param>
    /// <param name="completed"><see langword="true"/> if the stream was enumerated to its end.</param>
    private void RecordOutcome(
        Activity? activity,
        TagList tags,
        DateTimeOffset startTime,
        Exception? exception,
        bool completed
    )
    {
        var endTime = _timeProvider.GetUtcNow();
        var duration = (endTime - startTime).TotalMilliseconds;
        _ = activity?.SetEndTime(endTime.UtcDateTime);

        if (exception is not null)
        {
            var errorType = exception.GetType().FullName;

            // Capture comprehensive exception details in the activity
            _ = activity
                ?.SetStatus(ActivityStatusCode.Error, exception.Message)
                .SetTag(ErrorType, errorType)
                .SetTag(ExceptionType, errorType)
                .SetTag(ExceptionMessage, exception.Message)
                .SetTag(ExceptionStackTrace, exception.StackTrace)
                .SetTag(ExceptionTimestamp, endTime)
                .SetTag(Success, value: false);

            ErrorsCounter.Add(1, [.. tags, new(ErrorType, errorType)]);
            StreamQueryDurationHistogram.Record(duration, [.. tags, new(Success, false), new(ErrorType, errorType)]);
        }
        else if (completed)
        {
            // Status stays Unset on success, as required by the OpenTelemetry Trace API.
            _ = activity?.SetTag(ResponseTimestamp, endTime).SetTag(Success, value: true);

            StreamQueryDurationHistogram.Record(duration, [.. tags, new(Success, true)]);
        }
        else
        {
            // The consumer stopped early: not an error, so the status stays Unset.
            _ = activity?.SetTag(StreamCompleted, value: false);

            StreamQueryDurationHistogram.Record(duration, [.. tags, new(StreamCompleted, false)]);
        }
    }
}
