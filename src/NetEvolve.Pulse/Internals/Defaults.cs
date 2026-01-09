namespace NetEvolve.Pulse.Internals;

/// <summary>
/// Provides default values and constants used throughout the Pulse mediator implementation.
/// </summary>
internal static class Defaults
{
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
}
