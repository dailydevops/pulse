namespace NetEvolve.Pulse.Tests.Integration.Internals;

internal static class TestHelper
{
    internal static string TargetFramework =>
#if NET10_0
            "net10";
#elif NET9_0
            "net9";
#elif NET8_0
            "net8";
#endif
}
