namespace NetEvolve.Pulse;

using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

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
/// handled request type with a value-type request or response. They close the built-in open-generic interceptors that
/// are registered at the time of the call and register them, together with closed interceptors for the same request
/// type, as keyed services that the mediator resolves for value-type requests and responses. When an open-generic
/// interceptor that Pulse does not know is registered, nothing is registered for the request type, so resolving its
/// interceptors keeps failing instead of silently skipping that interceptor.
/// </para>
/// <para>
/// The methods do nothing while dynamic code is supported, because the DI container closes open-generic services over
/// value types itself.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NativeAotInterceptorExtensions
{
    /// <summary>
    /// The service key of the closed interceptor registrations.
    /// </summary>
    internal const string ServiceKey = "NetEvolve.Pulse.NativeAotInterceptors";

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
        where TCommand : ICommand<TResponse> => throw new NotImplementedException(typeof(TCommand).Name);

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
        where TCommand : IExclusiveCommand<TResponse> => throw new NotImplementedException(typeof(TCommand).Name);

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
        where TQuery : IQuery<TResponse> => throw new NotImplementedException(typeof(TQuery).Name);

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
        where TQuery : IStreamQuery<TResponse> => throw new NotImplementedException(typeof(TQuery).Name);

    internal static void AddCommandInterceptorsCore<TCommand, TResponse>(IServiceCollection services)
        where TCommand : ICommand<TResponse> => throw new NotImplementedException(typeof(TCommand).Name);

    internal static void AddExclusiveCommandInterceptorsCore<TCommand, TResponse>(IServiceCollection services)
        where TCommand : IExclusiveCommand<TResponse> => throw new NotImplementedException(typeof(TCommand).Name);

    internal static void AddQueryInterceptorsCore<TQuery, TResponse>(IServiceCollection services)
        where TQuery : IQuery<TResponse> => throw new NotImplementedException(typeof(TQuery).Name);

    internal static void AddStreamQueryInterceptorsCore<TQuery, TResponse>(IServiceCollection services)
        where TQuery : IStreamQuery<TResponse> => throw new NotImplementedException(typeof(TQuery).Name);
}
