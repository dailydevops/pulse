namespace NetEvolve.Pulse.Tests.Integration.Internals;

using TUnit.AspNetCore;

public abstract class WebApplicationTestBase<TEntryPoint>
    : WebApplicationTest<InternalTestWebApplicationFactory<TEntryPoint>, TEntryPoint>
    where TEntryPoint : class { }
