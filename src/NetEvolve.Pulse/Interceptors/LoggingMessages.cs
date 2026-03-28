namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Logging;

/// <summary>
/// Source-generated zero-allocation log message definitions used by the logging interceptors.
/// </summary>
internal static partial class LoggingMessages
{
    [LoggerMessage(Message = "Handling {RequestType} '{RequestName}' (CorrelationId: {CorrelationId})")]
    internal static partial void LogBeginRequest(
        this ILogger logger,
        LogLevel level,
        string requestType,
        string requestName,
        string? correlationId
    );

    [LoggerMessage(
        Message = "Handled {RequestType} '{RequestName}' in {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogEndRequest(
        this ILogger logger,
        LogLevel level,
        string requestType,
        string requestName,
        double elapsedMs,
        string? correlationId
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{RequestType} '{RequestName}' exceeded slow threshold of {ThresholdMs}ms, actual: {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogSlowRequest(
        this ILogger logger,
        string requestType,
        string requestName,
        double elapsedMs,
        double thresholdMs,
        string? correlationId
    );

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{RequestType} '{RequestName}' failed after {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogErrorRequest(
        this ILogger logger,
        Exception exception,
        string requestType,
        string requestName,
        double elapsedMs,
        string? correlationId
    );

    [LoggerMessage(Message = "Handling event '{EventName}' (CorrelationId: {CorrelationId})")]
    internal static partial void LogBeginEvent(
        this ILogger logger,
        LogLevel level,
        string eventName,
        string? correlationId
    );

    [LoggerMessage(Message = "Handled event '{EventName}' in {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})")]
    internal static partial void LogEndEvent(
        this ILogger logger,
        LogLevel level,
        string eventName,
        double elapsedMs,
        string? correlationId
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Event '{EventName}' exceeded slow threshold of {ThresholdMs}ms, actual: {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogSlowEvent(
        this ILogger logger,
        string eventName,
        double elapsedMs,
        double thresholdMs,
        string? correlationId
    );

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Event '{EventName}' failed after {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogErrorEvent(
        this ILogger logger,
        Exception exception,
        string eventName,
        double elapsedMs,
        string? correlationId
    );
}
