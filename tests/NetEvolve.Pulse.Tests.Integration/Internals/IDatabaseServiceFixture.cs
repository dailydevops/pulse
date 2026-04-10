namespace NetEvolve.Pulse.Tests.Integration.Internals;

using TUnit.Core.Interfaces;

public interface IDatabaseServiceFixture : IAsyncDisposable, IAsyncInitializer
{
    string ConnectionString { get; }

    DatabaseType DatabaseType { get; }
}

public interface IDatabaseInitializer
{
    ValueTask InitializeAsync(IDatabaseServiceFixture databaseService);
}

public sealed class EntityFrameworkInitializer : IDatabaseInitializer
{
    public ValueTask InitializeAsync(IDatabaseServiceFixture databaseService) => throw new NotImplementedException();
}

public sealed class AdoNetDatabaseInitializer : IDatabaseInitializer
{
    public ValueTask InitializeAsync(IDatabaseServiceFixture databaseService) => throw new NotImplementedException();
}
