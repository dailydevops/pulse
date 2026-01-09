namespace NetEvolve.Pulse;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register the Pulse mediator and its dependencies.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Pulse mediator implementation and its required dependencies into the service collection.
    /// The mediator is registered as a scoped service, allowing for request-scoped handlers and interceptors.
    /// </summary>
    /// <param name="services">The service collection to add the mediator to.</param>
    /// <param name="builder">An optional configuration action for customizing mediator behavior such as adding interceptors.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    /// <example>
    /// <code>
    /// services.AddPulseMediator(config =>
    /// {
    ///     config.AddActivityAndMetrics();
    /// });
    /// </code>
    /// </example>
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
