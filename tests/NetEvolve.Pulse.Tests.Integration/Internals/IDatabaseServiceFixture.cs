namespace NetEvolve.Pulse.Tests.Integration.Internals;

using TUnit.Core.Interfaces;

public interface IDatabaseServiceFixture : IAsyncDisposable, IAsyncInitializer
{
    string ConnectionString { get; }

    DatabaseType DatabaseType { get; }
}
