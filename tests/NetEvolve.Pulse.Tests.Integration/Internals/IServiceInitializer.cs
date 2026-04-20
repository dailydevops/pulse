namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

public interface IServiceInitializer
{
    void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture);

    ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken);

    void Initialize(IServiceCollection services, IServiceFixture serviceFixture);
}
