namespace NetEvolve.Pulse.Internals;

using System.Diagnostics.Metrics;

/// <summary>
/// Creates Pulse instruments with either the legacy units or the units of the OpenTelemetry semantic conventions,
/// as selected by <see cref="Interceptors.ActivityAndMetricsOptions.UseSemanticConventionUnits"/>.
/// </summary>
internal static class TelemetryUnits
{
    /// <summary>
    /// Bucket boundaries in seconds for duration histograms, taken from the OpenTelemetry HTTP semantic conventions.
    /// </summary>
    private static readonly double[] DurationBucketBoundariesInSeconds =
    [
        0.005,
        0.01,
        0.025,
        0.05,
        0.075,
        0.1,
        0.25,
        0.5,
        0.75,
        1,
        2.5,
        5,
        7.5,
        10,
    ];

    /// <summary>
    /// Creates a counter on <paramref name="meter"/> with the legacy or the UCUM annotation unit.
    /// </summary>
    /// <param name="meter">The meter that owns the counter.</param>
    /// <param name="name">The instrument name.</param>
    /// <param name="legacyUnit">The legacy unit, for example <c>requests</c>.</param>
    /// <param name="annotationUnit">The UCUM annotation unit, for example <c>{request}</c>.</param>
    /// <param name="description">The instrument description.</param>
    /// <param name="useSemanticConventionUnits">Whether to use the semantic convention unit.</param>
    /// <returns>The created counter.</returns>
    public static Counter<long> CreateCounter(
        Meter meter,
        string name,
        string legacyUnit,
        string annotationUnit,
        string description,
        bool useSemanticConventionUnits
    ) => meter.CreateCounter<long>(name, useSemanticConventionUnits ? annotationUnit : legacyUnit, description);

    /// <summary>
    /// Creates a duration histogram on <paramref name="meter"/> in milliseconds (<c>ms</c>) or seconds (<c>s</c>).
    /// </summary>
    /// <param name="meter">The meter that owns the histogram.</param>
    /// <param name="name">The instrument name.</param>
    /// <param name="subject">The measured operation used in the description, for example <c>request processing</c>.</param>
    /// <param name="useSemanticConventionUnits">Whether to record seconds.</param>
    /// <returns>The created histogram.</returns>
    public static Histogram<double> CreateDurationHistogram(
        Meter meter,
        string name,
        string subject,
        bool useSemanticConventionUnits
    )
    {
        if (!useSemanticConventionUnits)
        {
            return meter.CreateHistogram<double>(name, "ms", $"Duration of {subject} in milliseconds.");
        }

        return meter.CreateHistogram(
            name,
            "s",
            $"Duration of {subject} in seconds.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBucketBoundariesInSeconds }
        );
    }

    /// <summary>
    /// Converts <paramref name="elapsed"/> into the value recorded by a histogram created by
    /// <see cref="CreateDurationHistogram"/>.
    /// </summary>
    /// <param name="elapsed">The measured duration.</param>
    /// <param name="useSemanticConventionUnits">Whether the histogram records seconds.</param>
    /// <returns>The duration in seconds or milliseconds.</returns>
    public static double ToDuration(TimeSpan elapsed, bool useSemanticConventionUnits) =>
        useSemanticConventionUnits ? elapsed.TotalSeconds : elapsed.TotalMilliseconds;
}
