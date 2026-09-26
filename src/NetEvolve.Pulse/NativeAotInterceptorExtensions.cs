namespace NetEvolve.Pulse;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Registers closed variants of the built-in open-generic interceptors for a single request type, so that requests
/// with value-type responses, including <see cref="Extensibility.Void"/>, pass through them under NativeAOT.
/// </summary>
/// <remarks>
/// <para>
/// Under NativeAOT, <c>Microsoft.Extensions.DependencyInjection</c> refuses to close open-generic services over value
/// types. Resolving <see cref="IRequestInterceptor{TRequest, TResponse}"/> or
/// <see cref="IStreamQueryInterceptor{TQuery, TResponse}"/> therefore fails for such requests as soon as an open-generic
/// interceptor is registered.
/// </para>
/// <para>
/// The methods are called by the handler registrations that <c>NetEvolve.Pulse.SourceGeneration</c> generates, once per
/// handled request type with a value-type request or response. Applications call them for handlers that are
/// registered without the source generator. They close the built-in open-generic interceptors that are registered at
/// the time of the call and register them, together with closed interceptors for the same request type, as keyed
/// services that the mediator resolves for value-type requests and responses. When an open-generic interceptor that
/// Pulse does not know is registered, nothing is registered for the request type, so resolving its interceptors keeps
/// failing instead of silently skipping that interceptor.
/// </para>
/// <para>
/// Call the methods after <c>AddPulse</c> and after every other interceptor registration. When an interceptor for the
/// request type is registered, replaced or removed afterwards, the mediator throws an
/// <see cref="InvalidOperationException"/> for the request instead of skipping that interceptor.
/// </para>
/// <para>
/// The methods do nothing while dynamic code is supported, because the DI container closes open-generic services over
/// value types itself.
/// </para>
/// </remarks>
public static class NativeAotInterceptorExtensions
{
    /// <summary>
    /// The service key of the closed interceptor registrations.
    /// </summary>
    internal const string ServiceKey = "NetEvolve.Pulse.NativeAotInterceptors";

    /// <summary>
    /// The descriptor that the describe callbacks return for a built-in interceptor that does not apply to the request
    /// kind, for example the concurrent command guard for a query.
    /// </summary>
    private static readonly ServiceDescriptor NotApplicableDescriptor = new(
        typeof(INotApplicableInterceptor),
        new object()
    );

    /// <summary>
    /// Registers closed variants of the built-in request interceptors for the command type
    /// <typeparamref name="TCommand"/> under NativeAOT.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResponse">The response type of the command.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddNativeAotCommandInterceptors<TCommand, TResponse>(
        this IServiceCollection services
    )
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            AddCommandInterceptorsCore<TCommand, TResponse>(services);
        }

        return services;
    }

    /// <summary>
    /// Registers closed variants of the built-in request interceptors, including the concurrent command guard, for
    /// the exclusive command type <typeparamref name="TCommand"/> under NativeAOT.
    /// </summary>
    /// <typeparam name="TCommand">The exclusive command type.</typeparam>
    /// <typeparam name="TResponse">The response type of the command.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddNativeAotExclusiveCommandInterceptors<TCommand, TResponse>(
        this IServiceCollection services
    )
        where TCommand : IExclusiveCommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            AddExclusiveCommandInterceptorsCore<TCommand, TResponse>(services);
        }

        return services;
    }

    /// <summary>
    /// Registers closed variants of the built-in request interceptors, including query caching, for the query type
    /// <typeparamref name="TQuery"/> under NativeAOT.
    /// </summary>
    /// <typeparam name="TQuery">The query type.</typeparam>
    /// <typeparam name="TResponse">The response type of the query.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddNativeAotQueryInterceptors<TQuery, TResponse>(this IServiceCollection services)
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            AddQueryInterceptorsCore<TQuery, TResponse>(services);
        }

        return services;
    }

    /// <summary>
    /// Registers closed variants of the built-in stream query interceptors for the stream query type
    /// <typeparamref name="TQuery"/> under NativeAOT.
    /// </summary>
    /// <typeparam name="TQuery">The stream query type.</typeparam>
    /// <typeparam name="TResponse">The type of each item yielded by the stream query.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddNativeAotStreamQueryInterceptors<TQuery, TResponse>(
        this IServiceCollection services
    )
        where TQuery : IStreamQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            AddStreamQueryInterceptorsCore<TQuery, TResponse>(services);
        }

        return services;
    }

    internal static void AddCommandInterceptorsCore<TCommand, TResponse>(IServiceCollection services)
        where TCommand : ICommand<TResponse> =>
        AddKeyedInterceptors(
            services,
            typeof(IRequestInterceptor<,>),
            typeof(IRequestInterceptor<TCommand, TResponse>),
            DescribeRequestInterceptor<TCommand, TResponse>
        );

    internal static void AddExclusiveCommandInterceptorsCore<TCommand, TResponse>(IServiceCollection services)
        where TCommand : IExclusiveCommand<TResponse> =>
        AddKeyedInterceptors(
            services,
            typeof(IRequestInterceptor<,>),
            typeof(IRequestInterceptor<TCommand, TResponse>),
            static (openImplementationType, lifetime) =>
                DescribeKeyed(
                    typeof(IRequestInterceptor<TCommand, TResponse>),
                    openImplementationType == typeof(ConcurrentCommandGuardInterceptor<,>)
                        ? typeof(ConcurrentCommandGuardInterceptor<TCommand, TResponse>)
                        : CloseRequestInterceptor<TCommand, TResponse>(openImplementationType),
                    lifetime
                )
        );

    internal static void AddQueryInterceptorsCore<TQuery, TResponse>(IServiceCollection services)
        where TQuery : IQuery<TResponse> =>
        AddKeyedInterceptors(
            services,
            typeof(IRequestInterceptor<,>),
            typeof(IRequestInterceptor<TQuery, TResponse>),
            static (openImplementationType, lifetime) =>
                DescribeKeyed(
                    typeof(IRequestInterceptor<TQuery, TResponse>),
                    openImplementationType == typeof(DistributedCacheQueryInterceptor<,>)
                        ? typeof(DistributedCacheQueryInterceptor<TQuery, TResponse>)
                        : CloseRequestInterceptor<TQuery, TResponse>(openImplementationType),
                    lifetime
                )
        );

    internal static void AddStreamQueryInterceptorsCore<TQuery, TResponse>(IServiceCollection services)
        where TQuery : IStreamQuery<TResponse> =>
        AddKeyedInterceptors(
            services,
            typeof(IStreamQueryInterceptor<,>),
            typeof(IStreamQueryInterceptor<TQuery, TResponse>),
            static (openImplementationType, lifetime) =>
                DescribeKeyed(
                    typeof(IStreamQueryInterceptor<TQuery, TResponse>),
                    CloseStreamQueryInterceptor<TQuery, TResponse>(openImplementationType),
                    lifetime
                )
        );

    /// <summary>
    /// Registers the closed keyed interceptors for <paramref name="closedServiceType"/> and the marker that tells the
    /// mediator to resolve them, unless an unknown open-generic interceptor is registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="openServiceType">The open-generic interceptor service type.</param>
    /// <param name="closedServiceType">The closed interceptor service type for the request.</param>
    /// <param name="describe">
    /// Creates the closed keyed descriptor for a built-in open-generic implementation type and lifetime. Returns
    /// <see cref="NotApplicableDescriptor"/> when the built-in implementation does not apply to the request kind, and
    /// <see langword="null"/> when the implementation type is unknown.
    /// </param>
    private static void AddKeyedInterceptors(
        IServiceCollection services,
        Type openServiceType,
        Type closedServiceType,
        Func<Type, ServiceLifetime, ServiceDescriptor?> describe
    )
    {
        if (services.Any(d => d.ServiceType == typeof(Marker) && closedServiceType.Equals(d.ServiceKey)))
        {
            return;
        }

        var registrations = Marker.GetRegistrations(services, openServiceType, closedServiceType);

        if (registrations.Length == 0)
        {
            // Without interceptors, the unkeyed resolution works and still covers interceptors registered later.
            return;
        }

        var keyedDescriptors = new List<ServiceDescriptor>();

        foreach (var descriptor in registrations)
        {
            if (descriptor.ServiceType == openServiceType)
            {
                if (
                    descriptor.ImplementationType is not { } openImplementationType
                    || describe(openImplementationType, descriptor.Lifetime) is not { } keyedDescriptor
                )
                {
                    // An open-generic interceptor that cannot be closed here, e.g. a validation interceptor, keeps the
                    // resolution failing instead of being skipped silently.
                    return;
                }

                if (!ReferenceEquals(keyedDescriptor, NotApplicableDescriptor))
                {
                    keyedDescriptors.Add(keyedDescriptor);
                }
            }
            else
            {
                keyedDescriptors.Add(ToKeyed(descriptor));
            }
        }

        foreach (var keyedDescriptor in keyedDescriptors)
        {
            services.Add(keyedDescriptor);
        }

        services.Add(
            ServiceDescriptor.KeyedSingleton(
                closedServiceType,
                new Marker(services, openServiceType, closedServiceType, registrations)
            )
        );
    }

    private static ServiceDescriptor ToKeyed(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is { } instance)
        {
            return new ServiceDescriptor(descriptor.ServiceType, ServiceKey, instance);
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return new ServiceDescriptor(
                descriptor.ServiceType,
                ServiceKey,
                (serviceProvider, _) => factory(serviceProvider),
                descriptor.Lifetime
            );
        }

        return new ServiceDescriptor(
            descriptor.ServiceType,
            ServiceKey,
            descriptor.ImplementationType!,
            descriptor.Lifetime
        );
    }

    private static ServiceDescriptor? DescribeRequestInterceptor<TRequest, TResponse>(
        Type openImplementationType,
        ServiceLifetime lifetime
    )
        where TRequest : IRequest<TResponse> =>
        DescribeKeyed(
            typeof(IRequestInterceptor<TRequest, TResponse>),
            CloseRequestInterceptor<TRequest, TResponse>(openImplementationType),
            lifetime
        );

    /// <summary>
    /// Creates the keyed descriptor for a closed interceptor implementation type returned by
    /// <see cref="CloseRequestInterceptor{TRequest, TResponse}(Type)"/> or
    /// <see cref="CloseStreamQueryInterceptor{TQuery, TResponse}(Type)"/>.
    /// </summary>
    /// <param name="serviceType">The closed interceptor service type.</param>
    /// <param name="implementationType">
    /// The closed implementation type, <see cref="INotApplicableInterceptor"/>, or <see langword="null"/> for an unknown
    /// implementation type.
    /// </param>
    /// <param name="lifetime">The lifetime of the open-generic registration.</param>
    /// <returns>
    /// The keyed descriptor, <see cref="NotApplicableDescriptor"/> for <see cref="INotApplicableInterceptor"/>, or
    /// <see langword="null"/> for an unknown implementation type.
    /// </returns>
    private static ServiceDescriptor? DescribeKeyed(
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? implementationType,
        ServiceLifetime lifetime
    )
    {
        if (implementationType is null)
        {
            return null;
        }

        return implementationType == typeof(INotApplicableInterceptor)
            ? NotApplicableDescriptor
            : ServiceDescriptor.DescribeKeyed(serviceType, ServiceKey, implementationType, lifetime);
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private static Type? CloseRequestInterceptor<TRequest, TResponse>(Type openImplementationType)
        where TRequest : IRequest<TResponse>
    {
        if (openImplementationType == typeof(ActivityAndMetricsRequestInterceptor<,>))
        {
            return typeof(ActivityAndMetricsRequestInterceptor<TRequest, TResponse>);
        }

        if (openImplementationType == typeof(AuditRequestInterceptor<,>))
        {
            return typeof(AuditRequestInterceptor<TRequest, TResponse>);
        }

        if (openImplementationType == typeof(CacheInvalidationInterceptor<,>))
        {
            return typeof(CacheInvalidationInterceptor<TRequest, TResponse>);
        }

        if (openImplementationType == typeof(CommandDeadLetterInterceptor<,>))
        {
            return typeof(CommandDeadLetterInterceptor<TRequest, TResponse>);
        }

        if (openImplementationType == typeof(DataAnnotationsRequestInterceptor<,>))
        {
            return typeof(DataAnnotationsRequestInterceptor<TRequest, TResponse>);
        }

        if (openImplementationType == typeof(IdempotencyCommandInterceptor<,>))
        {
            return typeof(IdempotencyCommandInterceptor<TRequest, TResponse>);
        }

        if (openImplementationType == typeof(LoggingRequestInterceptor<,>))
        {
            return typeof(LoggingRequestInterceptor<TRequest, TResponse>);
        }

        if (openImplementationType == typeof(TimeoutRequestInterceptor<,>))
        {
            return typeof(TimeoutRequestInterceptor<TRequest, TResponse>);
        }

        if (
            openImplementationType == typeof(ConcurrentCommandGuardInterceptor<,>)
            || openImplementationType == typeof(DistributedCacheQueryInterceptor<,>)
        )
        {
            // The concurrent command guard and query caching only apply to exclusive commands and queries, which the
            // corresponding methods close.
            return typeof(INotApplicableInterceptor);
        }

        return null;
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private static Type? CloseStreamQueryInterceptor<TQuery, TResponse>(Type openImplementationType)
        where TQuery : IStreamQuery<TResponse>
    {
        if (openImplementationType == typeof(ActivityAndMetricsStreamQueryInterceptor<,>))
        {
            return typeof(ActivityAndMetricsStreamQueryInterceptor<TQuery, TResponse>);
        }

        if (openImplementationType == typeof(DataAnnotationsStreamQueryInterceptor<,>))
        {
            return typeof(DataAnnotationsStreamQueryInterceptor<TQuery, TResponse>);
        }

        if (openImplementationType == typeof(LoggingStreamQueryInterceptor<,>))
        {
            return typeof(LoggingStreamQueryInterceptor<TQuery, TResponse>);
        }

        if (openImplementationType == typeof(TimeoutStreamQueryInterceptor<,>))
        {
            return typeof(TimeoutStreamQueryInterceptor<TQuery, TResponse>);
        }

        return null;
    }

    /// <summary>
    /// Stands for a built-in interceptor that does not apply to the request kind.
    /// </summary>
    private interface INotApplicableInterceptor;

    /// <summary>
    /// Marks a closed interceptor service type, used as the service key, whose interceptors are registered as keyed
    /// services with <see cref="ServiceKey"/>, and detects interceptor registrations that changed afterwards.
    /// </summary>
    internal sealed class Marker
    {
        private readonly Type _openServiceType;
        private readonly Type _closedServiceType;
        private readonly ServiceDescriptor[] _registrations;
        private IServiceCollection? _services;

        /// <summary>
        /// Initializes a new instance of the <see cref="Marker"/> class.
        /// </summary>
        /// <param name="services">The service collection the keyed interceptors were registered in.</param>
        /// <param name="openServiceType">The open-generic interceptor service type.</param>
        /// <param name="closedServiceType">The closed interceptor service type for the request.</param>
        /// <param name="registrations">The unkeyed interceptor registrations the keyed interceptors were built from.</param>
        internal Marker(
            IServiceCollection services,
            Type openServiceType,
            Type closedServiceType,
            ServiceDescriptor[] registrations
        )
        {
            _services = services;
            _openServiceType = openServiceType;
            _closedServiceType = closedServiceType;
            _registrations = registrations;
        }

        /// <summary>
        /// Returns the unkeyed registrations of <paramref name="openServiceType"/> and
        /// <paramref name="closedServiceType"/> in registration order.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="openServiceType">The open-generic interceptor service type.</param>
        /// <param name="closedServiceType">The closed interceptor service type for the request.</param>
        /// <returns>The unkeyed interceptor registrations.</returns>
        internal static ServiceDescriptor[] GetRegistrations(
            IServiceCollection services,
            Type openServiceType,
            Type closedServiceType
        ) =>
            [
                .. services.Where(d =>
                    !d.IsKeyedService && (d.ServiceType == openServiceType || d.ServiceType == closedServiceType)
                ),
            ];

        /// <summary>
        /// Ensures that the interceptor registrations did not change after the keyed interceptors were registered.
        /// The check runs until it succeeds once.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if an interceptor was registered, replaced or removed after the keyed interceptors were registered,
        /// because the keyed interceptors would silently miss that change.
        /// </exception>
        internal void EnsureRegistrationsUnchanged()
        {
            if (Volatile.Read(ref _services) is not { } services)
            {
                return;
            }

            var registrations = GetRegistrations(services, _openServiceType, _closedServiceType);

            if (!registrations.SequenceEqual(_registrations))
            {
                var changes = string.Join(", ", registrations.Except(_registrations).Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"The registrations of '{_closedServiceType}' changed after its NativeAOT interceptor registration"
                        + (changes.Length == 0 ? "." : $": {changes}.")
                        + " Call the generated handler registration method, or the NativeAotInterceptorExtensions"
                        + " method, after all interceptor registrations."
                );
            }

            Volatile.Write(ref _services, null);
        }
    }
}
