namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a base interceptor for requests of type <typeparamref name="TRequest"/> that produce responses of type <typeparamref name="TResponse"/>.
/// Request interceptors enable cross-cutting concerns to be applied to both commands and queries in a unified manner.
/// Common use cases include logging, validation, metrics collection, and exception handling.
/// Multiple interceptors can be registered and will be executed in reverse order of registration (last registered runs first).
/// </summary>
/// <typeparam name="TRequest">The type of request to intercept, which must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
public interface IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Asynchronously intercepts the specified request, allowing pre- and post-processing around the handler invocation.
    /// The interceptor is responsible for calling the <paramref name="handler"/> delegate to continue the pipeline.
    /// Interceptors can short-circuit the pipeline by not calling the handler (e.g., for caching or validation failures).
    /// </summary>
    /// <param name="request">The request being processed.</param>
    /// <param name="handler">The next handler in the pipeline to invoke. Must be called to continue execution unless short-circuiting.</param>
    /// <returns>A task representing the asynchronous operation, containing the request response.</returns>
    Task<TResponse> HandleAsync(TRequest request, Func<TRequest, Task<TResponse>> handler);
}
