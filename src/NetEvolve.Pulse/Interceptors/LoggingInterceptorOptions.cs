namespace NetEvolve.Pulse;

using Microsoft.Extensions.Logging;

/// <summary>
/// Configuration options for the structured logging interceptors registered via <c>AddLogging()</c>.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// <code>
/// services.AddPulse(config =&gt; config.AddLogging(opts =&gt;
/// {
///     opts.SlowRequestThreshold = TimeSpan.FromMilliseconds(200);
///     opts.LogLevel = LogLevel.Information;
/// }));
/// </code>
/// </remarks>
public sealed class LoggingInterceptorOptions
{
    /// <summary>
    /// Gets or sets the log level used for begin and end log entries.
    /// Defaults to <see cref="LogLevel.Debug"/>.
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// Gets or sets the threshold above which a <see cref="LogLevel.Warning"/> entry is emitted
    /// to indicate a slow request or event. Set to <see langword="null"/> to disable slow-request detection.
    /// Defaults to 500 milliseconds.
    /// </summary>
    public TimeSpan? SlowRequestThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
}
