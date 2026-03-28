namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Internal interceptor that emits structured <see cref="ILogger"/>-based log entries for all events
/// processed by the mediator.
/// </summary>
/// <typeparam name="TEvent">The type of event being intercepted.</typeparam>
/// <remarks>
/// <para><strong>Log Entries:</strong></para>
/// <list type="bullet">
/// <item><description>Begin entry (configured level) before calling the handler.</description></item>
/// <item><description>End entry (configured level) after successful execution, including elapsed milliseconds.</description></item>
/// <item><description>Slow-event warning when elapsed time exceeds <see cref="LoggingInterceptorOptions.SlowRequestThreshold"/>.</description></item>
/// <item><description>Error entry with exception details when the handler throws; the exception is re-thrown.</description></item>
/// </list>
/// </remarks>
internal sealed class LoggingEventInterceptor<TEvent> : IEventInterceptor<TEvent>
    where TEvent : IEvent
{
    private readonly ILogger<LoggingEventInterceptor<TEvent>> _logger;
    private readonly LoggingInterceptorOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingEventInterceptor{TEvent}"/> class.
    /// </summary>
    /// <param name="logger">The logger for structured log output.</param>
    /// <param name="options">The options that control log levels and slow-event threshold.</param>
    /// <param name="timeProvider">The time provider used to measure elapsed time.</param>
    public LoggingEventInterceptor(
        ILogger<LoggingEventInterceptor<TEvent>> logger,
        IOptions<LoggingInterceptorOptions> options,
        TimeProvider timeProvider
    )
    {
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        TEvent message,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
    {
        var eventName = typeof(TEvent).Name;

        _logger.LogBeginHandle(_options.LogLevel, "Event", eventName, message.CorrelationId);

        var startTime = _timeProvider.GetUtcNow();

        try
        {
            await handler(message, cancellationToken).ConfigureAwait(false);

            var elapsedMs = (_timeProvider.GetUtcNow() - startTime).TotalMilliseconds;

            _logger.LogEndHandle(_options.LogLevel, "Event", eventName, elapsedMs, message.CorrelationId);

            if (
                _options.SlowRequestThreshold.HasValue
                && elapsedMs > _options.SlowRequestThreshold.Value.TotalMilliseconds
            )
            {
                _logger.LogSlowHandle(
                    "Event",
                    eventName,
                    elapsedMs,
                    _options.SlowRequestThreshold.Value.TotalMilliseconds,
                    message.CorrelationId
                );
            }
        }
        catch (Exception ex)
        {
            var elapsedMs = (_timeProvider.GetUtcNow() - startTime).TotalMilliseconds;
            _logger.LogErrorHandle(ex, "Event", eventName, elapsedMs, message.CorrelationId);
            throw;
        }
    }
}
