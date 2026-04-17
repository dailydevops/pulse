namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides extension methods on <see cref="IMediatorBuilder"/> for registering the
/// <see cref="ConcurrentCommandGuardInterceptor{TRequest,TResponse}"/>,
/// which enforces exclusive (non-concurrent) in-process execution for commands that implement
/// <see cref="IExclusiveCommand{TResponse}"/>.
/// </summary>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// The interceptor acquires a per-command-type <see cref="SemaphoreSlim"/>(1,1)
/// before delegating to the handler, ensuring at most one concurrent execution per command type.
/// The semaphore is always released in a <see langword="finally"/> block, even if the handler throws.
/// <para><strong>Scope:</strong></para>
/// Exclusivity is in-process only. For distributed exclusivity across multiple instances,
/// a distributed lock (e.g., Redis, SQL) is required.
/// </remarks>
public static class ConcurrentCommandGuardExtensions
{
    /// <summary>
    /// Registers an open-generic <see cref="ConcurrentCommandGuardInterceptor{TRequest,TResponse}"/>
    /// as a singleton <see cref="IRequestInterceptor{TRequest,TResponse}"/> for <em>all</em>
    /// <see cref="IExclusiveCommand{TResponse}"/> implementations discovered at runtime.
    /// </summary>
    /// <param name="configurator">The <see cref="IMediatorBuilder"/> to configure. Must not be <see langword="null"/>.</param>
    /// <returns>The same <paramref name="configurator"/> instance, enabling fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Registration:</strong></para>
    /// Uses <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// with a <see cref="ServiceLifetime.Singleton"/> lifetime to avoid duplicate registrations.
    /// <para><strong>Lifetime:</strong></para>
    /// The interceptor is registered as <see cref="ServiceLifetime.Singleton"/>,
    /// sharing one instance (and its per-command-type semaphore dictionary) for the full application lifetime.
    /// </remarks>
    /// <seealso cref="AddConcurrentCommandGuard{TRequest,TResponse}(IMediatorBuilder)"/>
    /// <seealso cref="AddConcurrentCommandGuard{TRequest}(IMediatorBuilder)"/>
    public static IMediatorBuilder AddConcurrentCommandGuard(this IMediatorBuilder configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(ConcurrentCommandGuardInterceptor<,>))
        );

        return configurator;
    }

    /// <summary>
    /// Registers a closed-generic <see cref="ConcurrentCommandGuardInterceptor{TRequest,TResponse}"/>
    /// as a singleton <see cref="IRequestInterceptor{TRequest,TResponse}"/> for the specific
    /// <typeparamref name="TRequest"/> / <typeparamref name="TResponse"/> pair.
    /// </summary>
    /// <typeparam name="TRequest">
    /// The command type to guard. Must implement <see cref="IExclusiveCommand{TResponse}"/>.
    /// </typeparam>
    /// <typeparam name="TResponse">The type of response produced by <typeparamref name="TRequest"/>.</typeparam>
    /// <param name="configurator">The <see cref="IMediatorBuilder"/> to configure. Must not be <see langword="null"/>.</param>
    /// <returns>The same <paramref name="configurator"/> instance, enabling fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Registration:</strong></para>
    /// Uses a two-step registration to guarantee a single shared interceptor instance:
    /// <list type="number">
    /// <item>
    /// <description>
    /// The open-generic <see cref="ConcurrentCommandGuardInterceptor{TRequest,TResponse}"/> is registered
    /// as a singleton mapped to itself via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAdd(IServiceCollection, ServiceDescriptor)"/>,
    /// ensuring at most one concrete instance per closed command type for the application lifetime.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="IRequestInterceptor{TRequest,TResponse}"/> is registered with a singleton factory
    /// via <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection, Func{IServiceProvider,TService})"/>
    /// that delegates to the concrete interceptor, so both the open-generic overload
    /// (<see cref="AddConcurrentCommandGuard(IMediatorBuilder)"/>) and this typed overload resolve
    /// to the <em>same</em> underlying instance and semaphore dictionary.
    /// </description>
    /// </item>
    /// </list>
    /// <para><strong>Lifetime:</strong></para>
    /// Both registrations use <see cref="ServiceLifetime.Singleton"/>, sharing one interceptor
    /// instance (and its semaphore dictionary) for the full application lifetime.
    /// </remarks>
    /// <seealso cref="AddConcurrentCommandGuard(IMediatorBuilder)"/>
    /// <seealso cref="AddConcurrentCommandGuard{TRequest}(IMediatorBuilder)"/>
    public static IMediatorBuilder AddConcurrentCommandGuard<TRequest, TResponse>(this IMediatorBuilder configurator)
        where TRequest : IExclusiveCommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        services.TryAdd(ServiceDescriptor.Singleton(typeof(ConcurrentCommandGuardInterceptor<,>)));
        services.TryAddSingleton<IRequestInterceptor<TRequest, TResponse>>(sp =>
            sp.GetRequiredService<ConcurrentCommandGuardInterceptor<TRequest, TResponse>>()
        );

        return configurator;
    }

    /// <summary>
    /// Registers a closed-generic <see cref="ConcurrentCommandGuardInterceptor{TRequest,TResponse}"/>
    /// as a singleton <see cref="IRequestInterceptor{TRequest,TResponse}"/> for the specific
    /// <typeparamref name="TRequest"/> whose response is <see cref="Extensibility.Void"/>.
    /// </summary>
    /// <typeparam name="TRequest">
    /// The void command type to guard. Must implement <see cref="IExclusiveCommand"/>, which is
    /// equivalent to <see cref="IExclusiveCommand{TResponse}"/> with <c>TResponse = <see cref="Extensibility.Void"/></c>.
    /// </typeparam>
    /// <param name="configurator">The <see cref="IMediatorBuilder"/> to configure. Must not be <see langword="null"/>.</param>
    /// <returns>The same <paramref name="configurator"/> instance, enabling fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This is a convenience overload of
    /// <see cref="AddConcurrentCommandGuard{TRequest,TResponse}(IMediatorBuilder)"/>
    /// that fixes <c>TResponse</c> to <see cref="Extensibility.Void"/>, simplifying registration
    /// for commands that do not produce a meaningful return value.
    /// </remarks>
    /// <seealso cref="AddConcurrentCommandGuard(IMediatorBuilder)"/>
    /// <seealso cref="AddConcurrentCommandGuard{TRequest,TResponse}(IMediatorBuilder)"/>
    public static IMediatorBuilder AddConcurrentCommandGuard<TRequest>(this IMediatorBuilder configurator)
        where TRequest : IExclusiveCommand<Extensibility.Void> =>
        configurator.AddConcurrentCommandGuard<TRequest, Extensibility.Void>();
}
