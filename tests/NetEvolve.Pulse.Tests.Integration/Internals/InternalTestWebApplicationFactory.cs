namespace NetEvolve.Pulse.Tests.Integration.Internals;

using TUnit.AspNetCore;

public sealed class InternalTestWebApplicationFactory<TEntryPoint> : TestWebApplicationFactory<TEntryPoint>
    where TEntryPoint : class { }
