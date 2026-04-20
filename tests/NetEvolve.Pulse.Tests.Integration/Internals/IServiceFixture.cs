namespace NetEvolve.Pulse.Tests.Integration.Internals;

using TUnit.Core.Interfaces;

public interface IServiceFixture : IAsyncDisposable, IAsyncInitializer
{
    string ConnectionString { get; }

    ServiceType ServiceType { get; }
}
