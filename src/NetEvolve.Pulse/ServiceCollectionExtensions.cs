namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPulseMediator(
        this IServiceCollection services,
        Action<IMediatorConfigurator>? builder = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        if (builder is not null)
        {
            var mediatorBuilder = new MediatorConfigurator(services);
            builder.Invoke(mediatorBuilder);
        }

        return services.AddScoped<IMediator, PulseMediator>();
    }
}
