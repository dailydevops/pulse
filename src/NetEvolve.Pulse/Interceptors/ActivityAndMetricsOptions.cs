namespace NetEvolve.Pulse.Interceptors;

/// <summary>
/// Options for the built-in activity and metrics telemetry registered via <c>AddActivityAndMetrics()</c>.
/// The options also apply to the metrics of the outbox processor.
/// </summary>
/// <example>
/// <code>
/// services.AddPulse(c =&gt; c.AddActivityAndMetrics(o =&gt; o.UseSemanticConventionUnits = true));
/// </code>
/// </example>
public sealed class ActivityAndMetricsOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether Pulse metrics use the units of the OpenTelemetry semantic conventions.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, duration histograms record seconds with unit <c>s</c> and explicit bucket
    /// boundaries, and counters use UCUM annotations (<c>{request}</c>, <c>{event}</c>, <c>{query}</c>,
    /// <c>{error}</c>, <c>{message}</c>). When <see langword="false"/> (default), durations are recorded in
    /// milliseconds with unit <c>ms</c> and counters keep their legacy units (<c>requests</c>, <c>events</c>,
    /// <c>queries</c>, <c>errors</c>, <c>messages</c>). Exporters such as Prometheus derive the exported metric
    /// name from the unit, so enabling this option changes the exported names. A later <c>0.x</c> release
    /// makes <see langword="true"/> the default.
    /// </remarks>
    public bool UseSemanticConventionUnits { get; set; }
}
