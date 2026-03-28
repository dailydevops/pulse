namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Logging;

/// <summary>
/// Source-generated zero-allocation log message definitions used by the logging interceptors.
/// </summary>
internal static partial class LoggingMessages
{
    [LoggerMessage(Message = "Handling {HandleAsyncType} '{HandleAsyncName}' (CorrelationId: {CorrelationId})")]
    internal static partial void LogBeginHandle(
        this ILogger logger,
        LogLevel level,
        string handleAsyncType,
        string handleAsyncName,
        string? correlationId
    );

    [LoggerMessage(
        Message = "Handled {HandleAsyncType} '{HandleAsyncName}' in {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogEndHandle(
        this ILogger logger,
        LogLevel level,
        string handleAsyncType,
        string handleAsyncName,
        double elapsedMs,
        string? correlationId
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{HandleAsyncType} '{HandleAsyncName}' exceeded slow threshold of {ThresholdMs}ms, actual: {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogSlowHandle(
        this ILogger logger,
        string handleAsyncType,
        string handleAsyncName,
        double elapsedMs,
        double thresholdMs,
        string? correlationId
    );

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{HandleAsyncType} '{HandleAsyncName}' failed after {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogErrorHandle(
        this ILogger logger,
        Exception exception,
        string handleAsyncType,
        string handleAsyncName,
        double elapsedMs,
        string? correlationId
    );
}
