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

    /// <summary>
    /// Logs the beginning of a stream query enumeration.
    /// </summary>
    /// <remarks>
    /// Emitted by <c>LoggingStreamQueryInterceptor</c> when enumeration of an <see cref="NetEvolve.Pulse.Extensibility.IStreamQuery{TResponse}"/> starts.
    /// </remarks>
    [LoggerMessage(Message = "Streaming '{StreamQueryName}' (CorrelationId: {CorrelationId})")]
    internal static partial void LogBeginStreamQuery(
        this ILogger logger,
        LogLevel level,
        string streamQueryName,
        string? correlationId
    );

    /// <summary>
    /// Logs the successful completion of a stream query enumeration.
    /// </summary>
    /// <remarks>
    /// Emitted by <c>LoggingStreamQueryInterceptor</c> when an <see cref="NetEvolve.Pulse.Extensibility.IStreamQuery{TResponse}"/> sequence is fully consumed.
    /// </remarks>
    [LoggerMessage(Message = "Streamed '{StreamQueryName}' in {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})")]
    internal static partial void LogEndStreamQuery(
        this ILogger logger,
        LogLevel level,
        string streamQueryName,
        double elapsedMs,
        string? correlationId
    );

    /// <summary>
    /// Logs a warning when a stream query exceeds the configured slow-stream threshold.
    /// </summary>
    /// <remarks>
    /// Emitted by <c>LoggingStreamQueryInterceptor</c> when an <see cref="NetEvolve.Pulse.Extensibility.IStreamQuery{TResponse}"/> exceeds the slow threshold.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{StreamQueryName} exceeded slow threshold of {ThresholdMs}ms, actual: {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogSlowStreamQuery(
        this ILogger logger,
        string streamQueryName,
        double elapsedMs,
        double thresholdMs,
        string? correlationId
    );

    /// <summary>
    /// Logs an error when a stream query fails during enumeration.
    /// </summary>
    /// <remarks>
    /// Emitted by <c>LoggingStreamQueryInterceptor</c> when an <see cref="NetEvolve.Pulse.Extensibility.IStreamQuery{TResponse}"/> throws an exception.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{StreamQueryName} failed after {ElapsedMs:F2}ms (CorrelationId: {CorrelationId})"
    )]
    internal static partial void LogErrorStreamQuery(
        this ILogger logger,
        Exception exception,
        string streamQueryName,
        double elapsedMs,
        string? correlationId
    );
}
