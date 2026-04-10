namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

public interface IDatabaseInitializer
{
    void Configure(IMediatorBuilder mediatorBuilder, IDatabaseServiceFixture databaseService);

    ValueTask<bool> CreateDatabaseAsync(IServiceProvider serviceProvider);

    void Initialize(IServiceCollection services, IDatabaseServiceFixture databaseService);
}
