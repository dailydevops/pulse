namespace NetEvolve.Pulse.Internals;

using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// Provides default values and constants used throughout the Pulse mediator implementation.
/// </summary>
internal static class Defaults
{
    /// <summary>
    /// Lazy-initialized backing field for the <see cref="ActivitySource"/> instance.
    /// Thread-safe initialization is ensured via <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>.
    /// </summary>
    private static readonly Lazy<ActivitySource> LazyActivitySource = new(
        () => new ActivitySource("NetEvolve.Pulse", Version),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// Gets the <see cref="System.Diagnostics.ActivitySource"/> used by the Pulse mediator for OpenTelemetry distributed tracing.
    /// The source name is <c>"NetEvolve.Pulse"</c> and the version matches the assembly version.
    /// </summary>
    public static ActivitySource ActivitySource => LazyActivitySource.Value;

    /// <summary>
    /// Lazy-initialized backing field for the <see cref="Meter"/> instance.
    /// Thread-safe initialization is ensured via <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>.
    /// </summary>
    private static readonly Lazy<Meter> LazyMeter = new(
        () => new Meter("NetEvolve.Pulse", Version),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// Gets the <see cref="System.Diagnostics.Metrics.Meter"/> used by the Pulse mediator for metrics collection.
    /// The meter name is <c>"NetEvolve.Pulse"</c> and the version matches the assembly version.
    /// </summary>
    public static Meter Meter => LazyMeter.Value;

    /// <summary>
    /// Gets the version of the Pulse library, extracted from the assembly's version information.
    /// This version is used for activity source and meter naming in telemetry.
    /// The version is cached after first retrieval for performance.
    /// </summary>
    public static string Version
    {
        get
        {
            // Use field-backed property with lazy initialization
            if (string.IsNullOrWhiteSpace(field))
            {
                var assembly = typeof(Defaults).Assembly;
                var version = assembly.GetName().Version;
                // Format as major.minor.patch (3 components)
                field = version?.ToString(3) ?? "1.0.0";
            }

            return field;
        }
    }

    /// <summary>
    /// Telemetry tag names for consistent labeling across OpenTelemetry activities and metrics.
    /// These constants ensure standardized naming conventions for distributed tracing and observability.
    /// </summary>
    internal static class Tags
    {
        // Exception-related tags
        /// <summary>Tag name for exception message.</summary>
        internal const string ExceptionMessage = "pulse.exception.message";

        /// <summary>Tag name for exception stack trace.</summary>
        internal const string ExceptionStackTrace = "pulse.exception.stacktrace";

        /// <summary>Tag name for exception occurrence timestamp.</summary>
        internal const string ExceptionTimestamp = "pulse.exception.timestamp";

        /// <summary>Tag name for exception type.</summary>
        internal const string ExceptionType = "pulse.exception.type";

        // Event-related tags
        /// <summary>Tag name for event correlation ID.</summary>
        internal const string EventCorrelationId = "pulse.event.correlation_id";

        /// <summary>Tag name for event name (type name).</summary>
        internal const string EventName = "pulse.event.name";

        /// <summary>Tag name for event start timestamp.</summary>
        internal const string EventTimestamp = "pulse.event.timestamp";

        /// <summary>Tag name for event type.</summary>
        internal const string EventType = "pulse.event.type";

        /// <summary>Tag name for event completion timestamp.</summary>
        internal const string EventCompletionTimestamp = "pulse.event.completion.timestamp";

        // Request-related tags
        /// <summary>Tag name for request correlation ID.</summary>
        internal const string RequestCorrelationId = "pulse.request.correlation_id";

        /// <summary>Tag name for request name (type name).</summary>
        internal const string RequestName = "pulse.request.name";

        /// <summary>Tag name for request start timestamp.</summary>
        internal const string RequestTimestamp = "pulse.request.timestamp";

        /// <summary>Tag name for request type (Command/Query/Request).</summary>
        internal const string RequestType = "pulse.request.type";

        // Response-related tags
        /// <summary>Tag name for response completion timestamp.</summary>
        internal const string ResponseTimestamp = "pulse.response.timestamp";

        /// <summary>Tag name for response type name.</summary>
        internal const string ResponseType = "pulse.response.type";

        // General tags
        /// <summary>Tag name for success/failure indicator.</summary>
        internal const string Success = "pulse.success";
    }
}
