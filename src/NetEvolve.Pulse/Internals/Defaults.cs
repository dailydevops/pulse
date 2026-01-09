namespace NetEvolve.Pulse.Internals;

internal static class Defaults
{
    public static string Version
    {
        get
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                var assembly = typeof(Defaults).Assembly;
                var version = assembly.GetName().Version;
                field = version?.ToString(3) ?? "1.0.0";
            }

            return field;
        }
    }
}
