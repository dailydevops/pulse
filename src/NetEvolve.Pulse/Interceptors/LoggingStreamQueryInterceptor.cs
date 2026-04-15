namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Internal interceptor that emits structured <see cref="ILogger"/>-based log entries for all stream queries
/// processed by the mediator.
/// </summary>
/// <typeparam name="TQuery">The type of stream query being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the stream query.</typeparam>
/// <remarks>
/// <para><strong>Log Entries:</strong></para>
/// <list type="bullet">
/// <item><description>Begin entry (configured level) before calling the handler.</description></item>
/// <item><description>End entry (configured level) after successful stream completion, including total elapsed milliseconds.</description></item>
/// <item><description>Slow-stream warning when total elapsed time exceeds <see cref="LoggingInterceptorOptions.SlowRequestThreshold"/>.</description></item>
/// <item><description>Error entry with exception details when the handler throws; the exception is re-thrown.</description></item>
/// </list>
/// </remarks>
internal sealed class LoggingStreamQueryInterceptor<TQuery, TResponse> : IStreamQueryInterceptor<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    private readonly ILogger<LoggingStreamQueryInterceptor<TQuery, TResponse>> _logger;
    private readonly LoggingInterceptorOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingStreamQueryInterceptor{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="logger">The logger for structured log output.</param>
    /// <param name="options">The options that control log levels and slow-request threshold.</param>
    /// <param name="timeProvider">The time provider used to measure elapsed time.</param>
    public LoggingStreamQueryInterceptor(
        ILogger<LoggingStreamQueryInterceptor<TQuery, TResponse>> logger,
        IOptions<LoggingInterceptorOptions> options,
        TimeProvider timeProvider
    )
    {
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var streamQueryName = typeof(TQuery).Name;

        _logger.LogBeginStreamQuery(_options.LogLevel, streamQueryName, request.CorrelationId);

        var startTime = _timeProvider.GetUtcNow();

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
            var elapsedMs = (_timeProvider.GetUtcNow() - startTime).TotalMilliseconds;
            _logger.LogErrorStreamQuery(ex, streamQueryName, elapsedMs, request.CorrelationId);
            caughtExceptionInfo.Throw();
        }
        else
        {
            var elapsedMs = (_timeProvider.GetUtcNow() - startTime).TotalMilliseconds;

            _logger.LogEndStreamQuery(_options.LogLevel, streamQueryName, elapsedMs, request.CorrelationId);

            if (
                _options.SlowRequestThreshold.HasValue
                && elapsedMs > _options.SlowRequestThreshold.Value.TotalMilliseconds
            )
            {
                _logger.LogSlowStreamQuery(
                    streamQueryName,
                    elapsedMs,
                    _options.SlowRequestThreshold.Value.TotalMilliseconds,
                    request.CorrelationId
                );
            }
        }
    }
}
