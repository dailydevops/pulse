namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Internal interceptor that emits structured <see cref="ILogger"/>-based log entries for all requests
/// (commands and queries) processed by the mediator.
/// </summary>
/// <typeparam name="TRequest">The type of request being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Log Entries:</strong></para>
/// <list type="bullet">
/// <item><description>Begin entry (configured level) before calling the handler.</description></item>
/// <item><description>End entry (configured level) after successful execution, including elapsed milliseconds.</description></item>
/// <item><description>Slow-request warning when elapsed time exceeds <see cref="LoggingInterceptorOptions.SlowRequestThreshold"/>.</description></item>
/// <item><description>Error entry with exception details when the handler throws; the exception is re-thrown.</description></item>
/// </list>
/// </remarks>
internal sealed class LoggingRequestInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingRequestInterceptor<TRequest, TResponse>> _logger;
    private readonly LoggingInterceptorOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="logger">The logger for structured log output.</param>
    /// <param name="options">The options that control log levels and slow-request threshold.</param>
    /// <param name="timeProvider">The time provider used to measure elapsed time.</param>
    public LoggingRequestInterceptor(
        ILogger<LoggingRequestInterceptor<TRequest, TResponse>> logger,
        IOptions<LoggingInterceptorOptions> options,
        TimeProvider timeProvider
    )
    {
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        var requestType = GetRequestType(request);
        var requestName = typeof(TRequest).Name;

        _logger.LogBeginHandle(_options.LogLevel, requestType, requestName, request.CorrelationId);

        var startTime = _timeProvider.GetUtcNow();

        try
        {
            var response = await handler(request, cancellationToken).ConfigureAwait(false);

            var elapsedMs = (_timeProvider.GetUtcNow() - startTime).TotalMilliseconds;

            _logger.LogEndHandle(_options.LogLevel, requestType, requestName, elapsedMs, request.CorrelationId);

            if (
                _options.SlowRequestThreshold.HasValue
                && elapsedMs > _options.SlowRequestThreshold.Value.TotalMilliseconds
            )
            {
                _logger.LogSlowHandle(
                    requestType,
                    requestName,
                    elapsedMs,
                    _options.SlowRequestThreshold.Value.TotalMilliseconds,
                    request.CorrelationId
                );
            }

            return response;
        }
        catch (Exception ex)
        {
            var elapsedMs = (_timeProvider.GetUtcNow() - startTime).TotalMilliseconds;
            _logger.LogErrorHandle(ex, requestType, requestName, elapsedMs, request.CorrelationId);
            throw;
        }
    }

    /// <summary>
    /// Determines the semantic category of the request.
    /// </summary>
    private static string GetRequestType(TRequest request) =>
        request switch
        {
            ICommand<TResponse> => "Command",
            IQuery<TResponse> => "Query",
            _ => "Request",
        };
}
