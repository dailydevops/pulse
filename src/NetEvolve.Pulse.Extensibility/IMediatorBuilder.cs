namespace NetEvolve.Pulse.Extensibility;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides a fluent interface for configuring the Pulse mediator with additional capabilities and interceptors.
/// This interface is used during service registration to customize mediator behavior.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// The configurator is passed as a delegate parameter to the <c>AddPulse</c> extension method,
/// allowing for fluent configuration of mediator features during service registration.
/// <para><strong>Extension Pattern:</strong></para>
/// This interface follows the builder/configurator pattern and can be extended with additional methods
/// via extension methods to add custom capabilities or third-party integrations.
/// <para><strong>Best Practices:</strong></para>
/// <list type="bullet">
/// <item><description>Configure all mediator features during startup</description></item>
/// <item><description>Use method chaining for cleaner configuration code</description></item>
/// <item><description>Add observability features (metrics, tracing) for production systems</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Basic configuration
/// services.AddPulse(config =>
/// {
///     config.AddActivityAndMetrics();
/// });
///
/// // Custom extension method example
/// public static class MediatorBuilderExtensions
/// {
///     public static IMediatorBuilder AddCustomValidation(this IMediatorBuilder configurator)
///     {
///         // Add validation interceptors
///         return configurator;
///     }
/// }
///
/// // Using custom extensions
/// services.AddPulse(config =>
/// {
///     config
///         .AddActivityAndMetrics()
///         .AddCustomValidation();
/// });
/// </code>
/// </example>
public interface IMediatorBuilder
{
    /// <summary>
    /// Gets the service collection for handler registration.
    /// </summary>
    /// <remarks>
    /// <para><strong>Purpose:</strong></para>
    /// Provides access to the underlying <see cref="IServiceCollection"/> to enable handler registration
    /// through extension methods. This supports both manual registration and automatic discovery patterns.
    /// <para><strong>Usage:</strong></para>
    /// This property is primarily used by extension methods to register handlers with specific lifetimes
    /// and to implement custom registration strategies (manual, assembly scanning, source-generated).
    /// <para><strong>Best Practices:</strong></para>
    /// <list type="bullet">
    /// <item><description>Use fluent extension methods for handler registration instead of direct service collection manipulation</description></item>
    /// <item><description>Consider AOT compatibility when choosing registration strategy</description></item>
    /// <item><description>Manual registration is AOT-safe and recommended for Native AOT scenarios</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Extension method using Services property
    /// public static IMediatorBuilder AddCommandHandler&lt;TCommand, TResponse, THandler&gt;(
    ///     this IMediatorBuilder configurator)
    ///     where TCommand : ICommand&lt;TResponse&gt;
    ///     where THandler : class, ICommandHandler&lt;TCommand, TResponse&gt;
    /// {
    ///     configurator.Services.AddScoped&lt;ICommandHandler&lt;TCommand, TResponse&gt;, THandler&gt;();
    ///     return configurator;
    /// }
    /// </code>
    /// </example>
    IServiceCollection Services { get; }
}
