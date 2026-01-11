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
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "As designed.")]
public static class AssemblyScanningExtensions
{
    extension(IMediatorConfigurator configurator)
    {
        /// <summary>
        /// Scans the specified assemblies for handler implementations and registers them.
        /// </summary>
        /// <param name="assemblies">The assemblies to scan for handlers.</param>
        /// <param name="lifetime">The service lifetime for discovered handlers (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown <paramref name="assemblies"/> is null.</exception>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use source generator-based registration or manual registration instead.</para>
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
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
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddHandlersFromAssemblies(
            Assembly[] assemblies,
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            ArgumentNullException.ThrowIfNull(configurator);
            ArgumentNullException.ThrowIfNull(assemblies);

            foreach (var assembly in assemblies.Where(a => a is not null))
            {
                configurator.RegisterHandlersFromAssembly(assembly, lifetime);
            }

            return configurator;
        }

        /// <summary>
        /// Scans the specified assembly for handler implementations and registers them.
        /// </summary>
        /// <param name="assembly">The assembly to scan for handlers.</param>
        /// <param name="lifetime">The service lifetime for discovered handlers (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="assembly"/> is null.</exception>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use source generator-based registration or manual registration instead.</para>
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
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
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddHandlersFromAssembly(
            Assembly assembly,
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            ArgumentNullException.ThrowIfNull(configurator);
            ArgumentNullException.ThrowIfNull(assembly);

            configurator.RegisterHandlersFromAssembly(assembly, lifetime);

            return configurator;
        }

        /// <summary>
        /// Scans the assembly containing the specified type for handler implementations and registers them.
        /// </summary>
        /// <typeparam name="TMarker">A type from the assembly to scan. Typically a handler type or marker interface.</typeparam>
        /// <param name="lifetime">The service lifetime for discovered handlers (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
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
        /// This method scans for all non-abstract classes that implement:
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
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddHandlersFromAssemblyContaining<TMarker>(
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            ArgumentNullException.ThrowIfNull(configurator);

            configurator.RegisterHandlersFromAssembly(typeof(TMarker).Assembly, lifetime);

            return configurator;
        }

        /// <summary>
        /// Scans the assembly of the caller (the assembly that invoked this method) for handler implementations and registers them.
        /// </summary>
        /// <param name="lifetime">The service lifetime for discovered handlers (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use source generator-based registration or manual registration instead.</para>
        /// <para><strong>What is the Calling Assembly?</strong></para>
        /// The calling assembly is the assembly that contains the code which directly invoked this method.
        /// This provides a convenient way to scan the assembly where your <c>AddPulse</c> configuration is defined.
        /// <para><strong>When to Use:</strong></para>
        /// Use this method when:
        /// <list type="bullet">
        /// <item><description>Your handlers are in the same assembly as your startup/configuration code</description></item>
        /// <item><description>You want to avoid specifying a marker type</description></item>
        /// <item><description>Your configuration is directly in the assembly you want to scan</description></item>
        /// </list>
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
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
        /// // In your Startup.cs or Program.cs:
        /// // This will scan the assembly containing your startup code
        /// services.AddPulse(config =&gt;
        /// {
        ///     config.AddHandlersFromCallingAssembly();
        /// });
        ///
        /// #pragma warning restore IL2026, IL3050
        /// </code>
        /// </example>
        [ExcludeFromCodeCoverage]
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddHandlersFromCallingAssembly(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            ArgumentNullException.ThrowIfNull(configurator);

            configurator.RegisterHandlersFromAssembly(Assembly.GetCallingAssembly(), lifetime);
            return configurator;
        }

        /// <summary>
        /// Scans the entry assembly (the application's startup assembly) for handler implementations and registers them.
        /// </summary>
        /// <param name="lifetime">The service lifetime for discovered handlers (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use source generator-based registration or manual registration instead.</para>
        /// <para><strong>What is the Entry Assembly?</strong></para>
        /// The entry assembly is the primary assembly that was executed when the application started.
        /// This is typically your <c>.exe</c> project (e.g., <c>MyApp.Web.dll</c> for ASP.NET Core applications
        /// or <c>MyApp.Console.exe</c> for console applications).
        /// <para><strong>When to Use:</strong></para>
        /// Use this method when:
        /// <list type="bullet">
        /// <item><description>Your handlers are in your main application project</description></item>
        /// <item><description>You have a single-assembly application with embedded handlers</description></item>
        /// <item><description>You want to avoid specifying a marker type from your entry project</description></item>
        /// </list>
        /// <para><strong>Null Safety:</strong></para>
        /// If <see cref="Assembly.GetEntryAssembly()"/> returns <c>null</c> (rare, but possible in certain hosting scenarios),
        /// this method safely returns without throwing an exception.
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
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
        /// // In your Startup.cs or Program.cs:
        /// // This will scan your application's main assembly (e.g., MyApp.Web.dll)
        /// services.AddPulse(config =&gt;
        /// {
        ///     config.AddHandlersFromEntryAssembly();
        /// });
        ///
        /// // Useful for applications where handlers are defined in the main project
        /// services.AddPulse(config =&gt;
        /// {
        ///     config
        ///         .AddHandlersFromEntryAssembly(ServiceLifetime.Scoped)
        ///         .AddActivityAndMetrics();
        /// });
        ///
        /// #pragma warning restore IL2026, IL3050
        /// </code>
        /// </example>
        [ExcludeFromCodeCoverage]
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddHandlersFromEntryAssembly(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            ArgumentNullException.ThrowIfNull(configurator);

            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is not null)
            {
                configurator.RegisterHandlersFromAssembly(entryAssembly, lifetime);
            }

            return configurator;
        }

        /// <summary>
        /// Scans the currently executing assembly for handler implementations and registers them.
        /// </summary>
        /// <param name="lifetime">The service lifetime for discovered handlers (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use source generator-based registration or manual registration instead.</para>
        /// <para><strong>What is the Executing Assembly?</strong></para>
        /// The executing assembly is the assembly that contains the code currently being executed.
        /// This is typically the assembly containing the <c>NetEvolve.Pulse</c> library itself.
        /// <para><strong>⚠️ IMPORTANT:</strong></para>
        /// In most application scenarios, you should use <see cref="AddHandlersFromAssemblyContaining{TMarker}"/>,
        /// <see cref="AddHandlersFromEntryAssembly"/>, or <see cref="AddHandlersFromCallingAssembly"/> instead.
        /// This method is primarily useful for testing or when building framework extensions.
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
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
        /// // This will scan the NetEvolve.Pulse assembly (usually not what you want)
        /// config.AddHandlersFromExecutingAssembly();
        ///
        /// #pragma warning restore IL2026, IL3050
        /// </code>
        /// </example>
        [ExcludeFromCodeCoverage]
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddHandlersFromExecutingAssembly(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            ArgumentNullException.ThrowIfNull(configurator);

            configurator.RegisterHandlersFromAssembly(Assembly.GetExecutingAssembly(), lifetime);
            return configurator;
        }

        /// <summary>
        /// Internal method that performs the actual reflection-based handler discovery and registration.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <param name="lifetime">The service lifetime for discovered handlers.</param>
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        private void RegisterHandlersFromAssembly(Assembly assembly, ServiceLifetime lifetime)
        {
            var services = configurator.Services;
            var handlerInterfaces = new[]
            {
                typeof(ICommandHandler<,>),
                typeof(IQueryHandler<,>),
                typeof(IEventHandler<>),
            };

            // Get all types that are:
            // - Classes (not interfaces or structs)
            // - Not abstract
            // - Not generic type definitions (open generics)
            var types = GetAllTypesFromAssembly(assembly)
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

        /// <summary>
        /// Scans the specified assemblies for interceptor implementations and registers them.
        /// </summary>
        /// <param name="assemblies">The assemblies to scan for interceptors.</param>
        /// <param name="lifetime">The service lifetime for discovered interceptors (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="assemblies"/> is null.</exception>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use manual registration instead.</para>
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
        /// <list type="bullet">
        /// <item><description><see cref="IRequestInterceptor{TRequest, TResponse}"/></description></item>
        /// <item><description><see cref="ICommandInterceptor{TCommand, TResponse}"/></description></item>
        /// <item><description><see cref="IQueryInterceptor{TQuery, TResponse}"/></description></item>
        /// <item><description><see cref="IEventInterceptor{TEvent}"/></description></item>
        /// </list>
        /// <para><strong>Execution Order:</strong></para>
        /// Interceptors execute in reverse order of registration (LIFO). Be mindful of registration order.
        /// </remarks>
        /// <example>
        /// <code>
        /// #pragma warning disable IL2026, IL3050
        ///
        /// var assemblies = new[]
        /// {
        ///     typeof(LoggingInterceptor&lt;,&gt;).Assembly,
        ///     typeof(ValidationInterceptor&lt;,&gt;).Assembly
        /// };
        ///
        /// config.AddInterceptorsFromAssemblies(assemblies);
        ///
        /// #pragma warning restore IL2026, IL3050
        /// </code>
        /// </example>
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddInterceptorsFromAssemblies(
            Assembly[] assemblies,
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            ArgumentNullException.ThrowIfNull(configurator);
            ArgumentNullException.ThrowIfNull(assemblies);

            foreach (var assembly in assemblies.Where(a => a is not null))
            {
                configurator.RegisterInterceptorsFromAssembly(assembly, lifetime);
            }

            return configurator;
        }

        /// <summary>
        /// Scans the specified assembly for interceptor implementations and registers them.
        /// </summary>
        /// <param name="assembly">The assembly to scan for interceptors.</param>
        /// <param name="lifetime">The service lifetime for discovered interceptors (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="assembly"/> is null.</exception>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use manual registration instead.</para>
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
        /// <list type="bullet">
        /// <item><description><see cref="IRequestInterceptor{TRequest, TResponse}"/></description></item>
        /// <item><description><see cref="ICommandInterceptor{TCommand, TResponse}"/></description></item>
        /// <item><description><see cref="IQueryInterceptor{TQuery, TResponse}"/></description></item>
        /// <item><description><see cref="IEventInterceptor{TEvent}"/></description></item>
        /// </list>
        /// <para><strong>Generic Type Definitions:</strong></para>
        /// Open generic interceptor types (e.g., <c>LoggingInterceptor&lt;,&gt;</c>) are excluded from scanning.
        /// Only closed generic types are registered.
        /// </remarks>
        /// <example>
        /// <code>
        /// #pragma warning disable IL2026, IL3050
        ///
        /// var interceptorsAssembly = Assembly.Load("MyApp.Interceptors");
        /// config.AddInterceptorsFromAssembly(interceptorsAssembly);
        ///
        /// #pragma warning restore IL2026, IL3050
        /// </code>
        /// </example>
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddInterceptorsFromAssembly(
            Assembly assembly,
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            ArgumentNullException.ThrowIfNull(configurator);
            ArgumentNullException.ThrowIfNull(assembly);

            configurator.RegisterInterceptorsFromAssembly(assembly, lifetime);

            return configurator;
        }

        /// <summary>
        /// Scans the assembly containing the specified type for interceptor implementations and registers them.
        /// </summary>
        /// <typeparam name="TMarker">A type from the assembly to scan. Typically an interceptor type or marker interface.</typeparam>
        /// <param name="lifetime">The service lifetime for discovered interceptors (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use manual registration instead.</para>
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
        /// <list type="bullet">
        /// <item><description><see cref="IRequestInterceptor{TRequest, TResponse}"/></description></item>
        /// <item><description><see cref="ICommandInterceptor{TCommand, TResponse}"/></description></item>
        /// <item><description><see cref="IQueryInterceptor{TQuery, TResponse}"/></description></item>
        /// <item><description><see cref="IEventInterceptor{TEvent}"/></description></item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// #pragma warning disable IL2026, IL3050
        ///
        /// // Scan the assembly containing LoggingInterceptor
        /// config.AddInterceptorsFromAssemblyContaining&lt;LoggingInterceptor&lt;,&gt;&gt;();
        ///
        /// #pragma warning restore IL2026, IL3050
        /// </code>
        /// </example>
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddInterceptorsFromAssemblyContaining<TMarker>(
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            ArgumentNullException.ThrowIfNull(configurator);

            configurator.RegisterInterceptorsFromAssembly(typeof(TMarker).Assembly, lifetime);

            return configurator;
        }

        /// <summary>
        /// Scans the assembly of the caller for interceptor implementations and registers them.
        /// </summary>
        /// <param name="lifetime">The service lifetime for discovered interceptors (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use manual registration instead.</para>
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
        /// <list type="bullet">
        /// <item><description><see cref="IRequestInterceptor{TRequest, TResponse}"/></description></item>
        /// <item><description><see cref="ICommandInterceptor{TCommand, TResponse}"/></description></item>
        /// <item><description><see cref="IQueryInterceptor{TQuery, TResponse}"/></description></item>
        /// <item><description><see cref="IEventInterceptor{TEvent}"/></description></item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// #pragma warning disable IL2026, IL3050
        ///
        /// services.AddPulse(config =>
        /// {
        ///     config.AddInterceptorsFromCallingAssembly();
        /// });
        ///
        /// #pragma warning restore IL2026, IL3050
        /// </code>
        /// </example>
        [ExcludeFromCodeCoverage]
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddInterceptorsFromCallingAssembly(
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            ArgumentNullException.ThrowIfNull(configurator);

            configurator.RegisterInterceptorsFromAssembly(Assembly.GetCallingAssembly(), lifetime);
            return configurator;
        }

        /// <summary>
        /// Scans the entry assembly for interceptor implementations and registers them.
        /// </summary>
        /// <param name="lifetime">The service lifetime for discovered interceptors (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use manual registration instead.</para>
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
        /// <list type="bullet">
        /// <item><description><see cref="IRequestInterceptor{TRequest, TResponse}"/></description></item>
        /// <item><description><see cref="ICommandInterceptor{TCommand, TResponse}"/></description></item>
        /// <item><description><see cref="IQueryInterceptor{TQuery, TResponse}"/></description></item>
        /// <item><description><see cref="IEventInterceptor{TEvent}"/></description></item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// #pragma warning disable IL2026, IL3050
        ///
        /// services.AddPulse(config =>
        /// {
        ///     config.AddInterceptorsFromEntryAssembly();
        /// });
        ///
        /// #pragma warning restore IL2026, IL3050
        /// </code>
        /// </example>
        [ExcludeFromCodeCoverage]
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddInterceptorsFromEntryAssembly(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            ArgumentNullException.ThrowIfNull(configurator);

            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is not null)
            {
                configurator.RegisterInterceptorsFromAssembly(entryAssembly, lifetime);
            }

            return configurator;
        }

        /// <summary>
        /// Scans the currently executing assembly for interceptor implementations and registers them.
        /// </summary>
        /// <param name="lifetime">The service lifetime for discovered interceptors (default: Scoped).</param>
        /// <returns>The configurator for method chaining.</returns>
        /// <remarks>
        /// <para><strong>⚠️ WARNING:</strong> This method uses reflection and is NOT compatible with Native AOT or IL trimming.</para>
        /// <para>For AOT scenarios, use manual registration instead.</para>
        /// <para><strong>⚠️ IMPORTANT:</strong></para>
        /// In most scenarios, use <see cref="AddInterceptorsFromAssemblyContaining{TMarker}"/> instead.
        /// This method is primarily useful for testing or framework extensions.
        /// <para><strong>Discovery Rules:</strong></para>
        /// This method scans for all non-abstract classes that implement:
        /// <list type="bullet">
        /// <item><description><see cref="IRequestInterceptor{TRequest, TResponse}"/></description></item>
        /// <item><description><see cref="ICommandInterceptor{TCommand, TResponse}"/></description></item>
        /// <item><description><see cref="IQueryInterceptor{TQuery, TResponse}"/></description></item>
        /// <item><description><see cref="IEventInterceptor{TEvent}"/></description></item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// #pragma warning disable IL2026, IL3050
        ///
        /// config.AddInterceptorsFromExecutingAssembly();
        ///
        /// #pragma warning restore IL2026, IL3050
        /// </code>
        /// </example>
        [ExcludeFromCodeCoverage]
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        public IMediatorConfigurator AddInterceptorsFromExecutingAssembly(
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            ArgumentNullException.ThrowIfNull(configurator);

            configurator.RegisterInterceptorsFromAssembly(Assembly.GetExecutingAssembly(), lifetime);
            return configurator;
        }

        /// <summary>
        /// Internal method that performs the actual reflection-based interceptor discovery and registration.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <param name="lifetime">The service lifetime for discovered interceptors.</param>
        [RequiresUnreferencedCode(
            "Assembly scanning uses reflection and is not compatible with IL trimming or Native AOT."
        )]
        [RequiresDynamicCode(
            "Assembly scanning requires dynamic code generation and is not compatible with Native AOT."
        )]
        private void RegisterInterceptorsFromAssembly(Assembly assembly, ServiceLifetime lifetime)
        {
            var services = configurator.Services;
            var interceptorInterfaces = new[]
            {
                typeof(IRequestInterceptor<,>),
                typeof(ICommandInterceptor<,>),
                typeof(IQueryInterceptor<,>),
                typeof(IEventInterceptor<>),
            };

            // Get all types that are:
            // - Classes (not interfaces or structs)
            // - Not abstract
            // - Not generic type definitions (open generics)
            var types = GetAllTypesFromAssembly(assembly)
                .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

            foreach (var type in types)
            {
                // Find all interceptor interfaces implemented by this type
                var interfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType && interceptorInterfaces.Contains(i.GetGenericTypeDefinition()));

                // Register each interceptor interface implementation
                foreach (var @interface in interfaces)
                {
                    services.Add(new ServiceDescriptor(@interface, type, lifetime));
                }
            }
        }
    }

    private static Type[] GetAllTypesFromAssembly(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // If some types fail to load, use only the types that loaded successfully
            return [.. ex.Types.Where(t => t is not null)!];
        }
    }
}
