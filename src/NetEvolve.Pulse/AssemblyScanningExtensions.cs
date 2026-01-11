namespace NetEvolve.Pulse;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Provides extension methods for automatic handler discovery via reflection.
/// </summary>
/// <remarks>
/// <para><strong>⚠️ WARNING:</strong> Assembly scanning is NOT compatible with Native AOT or IL trimming.</para>
/// <para><strong>AOT Alternative:</strong></para>
/// For Native AOT scenarios (Blazor WebAssembly, Native AOT compilation), use source generator-based
/// registration instead by referencing the <c>NetEvolve.Pulse.Generators</c> package.
/// <para><strong>Performance Impact:</strong></para>
/// Assembly scanning uses reflection at startup, which adds initialization overhead. For applications
/// with strict startup time requirements, consider manual registration or source generator-based registration.
/// <para><strong>When to Use:</strong></para>
/// <list type="bullet">
/// <item><description>Traditional .NET applications without AOT requirements</description></item>
/// <item><description>Rapid prototyping and development scenarios</description></item>
/// <item><description>Applications with many handlers where manual registration would be tedious</description></item>
/// </list>
/// <para><strong>When NOT to Use:</strong></para>
/// <list type="bullet">
/// <item><description>Blazor WebAssembly applications (use source generator instead)</description></item>
/// <item><description>Native AOT compilation scenarios (use manual or source generator registration)</description></item>
/// <item><description>Applications requiring IL trimming (use manual or source generator registration)</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Suppress AOT warnings if you're certain this is not an AOT scenario
/// #pragma warning disable IL2026, IL3050
///
/// services.AddPulse(config =>
/// {
///     config
///         .AddHandlersFromAssemblyContaining&lt;CreateOrderCommandHandler&gt;()
///         .AddHandlersFromAssembly(typeof(ExternalHandlers).Assembly)
///         .AddActivityAndMetrics();
/// });
///
/// #pragma warning restore IL2026, IL3050
/// </code>
/// </example>
public static class AssemblyScanningExtensions
{
    /// <summary>
    /// Scans the specified assemblies for handler implementations and registers them.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="assemblies">The assemblies to scan for handlers.</param>
    /// <param name="lifetime">The service lifetime for discovered handlers (default: Scoped).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator"/> or <paramref name="assemblies"/> is null.</exception>
    /// <remarks>
    /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
    /// <para>For AOT scenarios, use source generator-based registration or manual registration instead.</para>
    /// <para><strong>Discovery Rules:</strong></para>
    /// This method scans for all public, non-abstract classes that implement:
    /// <list type="bullet">
    /// <item><description><see cref="ICommandHandler{TCommand, TResponse}"/></description></item>
    /// <item><description><see cref="IQueryHandler{TQuery, TResponse}"/></description></item>
    /// <item><description><see cref="IEventHandler{TEvent}"/></description></item>
    /// </list>
    /// <para><strong>Lifetime Management:</strong></para>
    /// All discovered handlers are registered with the same lifetime. For mixed lifetimes,
    /// use multiple scanning calls with different lifetime parameters or use manual registration.
    /// </remarks>
    /// <example>
    /// <code>
    /// #pragma warning disable IL2026, IL3050
    ///
    /// var assemblies = new[]
    /// {
    ///     typeof(OrderHandlers).Assembly,
    ///     typeof(ProductHandlers).Assembly,
    ///     typeof(CustomerHandlers).Assembly
    /// };
    ///
    /// config.AddHandlersFromAssemblies(assemblies, ServiceLifetime.Scoped);
    ///
    /// #pragma warning restore IL2026, IL3050
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
    )]
    [RequiresDynamicCode("Assembly scanning requires dynamic code generation and is not compatible with Native AOT.")]
    public static IMediatorConfigurator AddHandlersFromAssemblies(
        this IMediatorConfigurator configurator,
        Assembly[] assemblies,
        ServiceLifetime lifetime = ServiceLifetime.Scoped
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            RegisterHandlersFromAssembly(configurator.Services, assembly, lifetime);
        }

        return configurator;
    }

    /// <summary>
    /// Scans the specified assembly for handler implementations and registers them.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="assembly">The assembly to scan for handlers.</param>
    /// <param name="lifetime">The service lifetime for discovered handlers (default: Scoped).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator"/> or <paramref name="assembly"/> is null.</exception>
    /// <remarks>
    /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
    /// <para>For AOT scenarios, use source generator-based registration or manual registration instead.</para>
    /// <para><strong>Discovery Rules:</strong></para>
    /// This method scans for all public, non-abstract classes that implement:
    /// <list type="bullet">
    /// <item><description><see cref="ICommandHandler{TCommand, TResponse}"/></description></item>
    /// <item><description><see cref="IQueryHandler{TQuery, TResponse}"/></description></item>
    /// <item><description><see cref="IEventHandler{TEvent}"/></description></item>
    /// </list>
    /// <para><strong>Generic Type Definitions:</strong></para>
    /// Open generic handler types (e.g., <c>MyHandler&lt;T&gt;</c>) are excluded from scanning.
    /// Only closed generic types are registered.
    /// </remarks>
    /// <example>
    /// <code>
    /// #pragma warning disable IL2026, IL3050
    ///
    /// var handlersAssembly = Assembly.Load("MyApp.Handlers");
    /// config.AddHandlersFromAssembly(handlersAssembly);
    ///
    /// #pragma warning restore IL2026, IL3050
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
    )]
    [RequiresDynamicCode("Assembly scanning requires dynamic code generation and is not compatible with Native AOT.")]
    public static IMediatorConfigurator AddHandlersFromAssembly(
        this IMediatorConfigurator configurator,
        Assembly assembly,
        ServiceLifetime lifetime = ServiceLifetime.Scoped
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(assembly);

        RegisterHandlersFromAssembly(configurator.Services, assembly, lifetime);

        return configurator;
    }

    /// <summary>
    /// Scans the assembly containing the specified type for handler implementations and registers them.
    /// </summary>
    /// <typeparam name="TMarker">A type from the assembly to scan. Typically a handler type or marker interface.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="lifetime">The service lifetime for discovered handlers (default: Scoped).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator"/> is null.</exception>
    /// <remarks>
    /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
    /// <para>For AOT scenarios, use source generator-based registration or manual registration instead.</para>
    /// <para><strong>Marker Type Selection:</strong></para>
    /// Choose a type that is:
    /// <list type="bullet">
    /// <item><description>Located in the same assembly as your handlers</description></item>
    /// <item><description>Unlikely to move to a different assembly during refactoring</description></item>
    /// <item><description>Representative of the handler assembly (e.g., a base handler class or marker interface)</description></item>
    /// </list>
    /// <para><strong>Discovery Rules:</strong></para>
    /// This method scans for all public, non-abstract classes that implement:
    /// <list type="bullet">
    /// <item><description><see cref="ICommandHandler{TCommand, TResponse}"/></description></item>
    /// <item><description><see cref="IQueryHandler{TQuery, TResponse}"/></description></item>
    /// <item><description><see cref="IEventHandler{TEvent}"/></description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// #pragma warning disable IL2026, IL3050
    ///
    /// // Scan the assembly containing CreateOrderCommandHandler
    /// config.AddHandlersFromAssemblyContaining&lt;CreateOrderCommandHandler&gt;();
    ///
    /// // Scan the assembly containing a marker interface
    /// config.AddHandlersFromAssemblyContaining&lt;IHandlerMarker&gt;();
    ///
    /// #pragma warning restore IL2026, IL3050
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
    )]
    [RequiresDynamicCode("Assembly scanning requires dynamic code generation and is not compatible with Native AOT.")]
    public static IMediatorConfigurator AddHandlersFromAssemblyContaining<TMarker>(
        this IMediatorConfigurator configurator,
        ServiceLifetime lifetime = ServiceLifetime.Scoped
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        RegisterHandlersFromAssembly(configurator.Services, typeof(TMarker).Assembly, lifetime);

        return configurator;
    }

    /// <summary>
    /// Internal method that performs the actual reflection-based handler discovery and registration.
    /// </summary>
    /// <param name="services">The service collection to register handlers into.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="lifetime">The service lifetime for discovered handlers.</param>
    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
    )]
    [RequiresDynamicCode("Assembly scanning requires dynamic code generation and is not compatible with Native AOT.")]
    private static void RegisterHandlersFromAssembly(
        IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime
    )
    {
        var handlerInterfaces = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IEventHandler<>) };

        // Get all types that are:
        // - Classes (not interfaces or structs)
        // - Not abstract
        // - Not generic type definitions (open generics)
        var types = assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var type in types)
        {
            // Find all handler interfaces implemented by this type
            var interfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition()));

            // Register each handler interface implementation
            foreach (var @interface in interfaces)
            {
                services.Add(new ServiceDescriptor(@interface, type, lifetime));
            }
        }
    }
}
