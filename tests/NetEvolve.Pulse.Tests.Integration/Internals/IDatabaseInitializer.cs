namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

public interface IDatabaseInitializer
{
    void Configure(IMediatorBuilder mediatorBuilder, IServiceType databaseService);

    ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken);

    void Initialize(IServiceCollection services, IServiceType databaseService);
}
