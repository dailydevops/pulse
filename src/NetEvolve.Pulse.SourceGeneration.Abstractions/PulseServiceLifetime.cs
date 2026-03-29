namespace NetEvolve.Pulse.SourceGeneration;

/// <summary>
/// Specifies the lifetime of a service registered by the Pulse source generator.
/// </summary>
/// <remarks>
/// This enum mirrors <c>Microsoft.Extensions.DependencyInjection.ServiceLifetime</c> so that
/// the <see cref="PulseHandlerAttribute"/> does not require a dependency on the DI abstractions package.
/// </remarks>
public enum PulseServiceLifetime
{
    /// <summary>
    /// Specifies that a single instance of the service will be created.
    /// </summary>
    Singleton = 0,

    /// <summary>
    /// Specifies that a new instance of the service will be created for each scope.
    /// </summary>
    Scoped = 1,

    /// <summary>
    /// Specifies that a new instance of the service will be created every time it is requested.
    /// </summary>
    Transient = 2,
}
