namespace NetEvolve.Pulse.Internals;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;

internal sealed class MediatorConfigurator : IMediatorConfigurator
{
    private readonly IServiceCollection _services;

    public MediatorConfigurator(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public IMediatorConfigurator AddActivityAndMetrics()
    {
        _services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(ActivityAndMetricsInterceptor<,>))
        );

        return this;
    }
}
