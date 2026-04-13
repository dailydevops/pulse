namespace NetEvolve.Pulse.Tests.Integration.Internals;

internal static class TestHelper
{
    internal static string TargetFramework
    {
        get
        {
#if NET10_0
            return "net10";
#elif NET9_0
            return "net9";
#elif NET8_0
            return "net8";
#endif
        }
    }
}
