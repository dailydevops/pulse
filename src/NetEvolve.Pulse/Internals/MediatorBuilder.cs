namespace NetEvolve.Pulse.Internals;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Internal implementation of <see cref="IMediatorBuilder"/> that provides fluent configuration capabilities for the Pulse mediator.
/// This class is used during service registration to add interceptors and other cross-cutting concerns.
/// </summary>
internal sealed class MediatorBuilder : IMediatorBuilder
{
    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public MediatorBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }
}
