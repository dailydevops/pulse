namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Http.Correlation.Abstractions;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Request interceptor that propagates the HTTP correlation ID from <see cref="IHttpCorrelationAccessor"/>
/// into every <see cref="IRequest{TResponse}"/> dispatched through the mediator.
/// </summary>
/// <typeparam name="TRequest">The type of request to intercept, which must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Propagation Logic:</strong></para>
/// Before invoking the next handler delegate, the interceptor sets <c>request.CorrelationId</c> from
/// <see cref="IHttpCorrelationAccessor.CorrelationId"/> only when both of the following conditions hold:
/// <list type="bullet">
/// <item><description><c>request.CorrelationId</c> is <see langword="null"/> or empty.</description></item>
/// <item><description><see cref="IHttpCorrelationAccessor.CorrelationId"/> is non-<see langword="null"/> and non-empty.</description></item>
/// </list>
/// <para><strong>Optional Dependency:</strong></para>
/// If <see cref="IHttpCorrelationAccessor"/> is not registered in the DI container (for example in a
/// background-service context), the interceptor passes through without modification or error.
/// <para><strong>Lifetime Consideration:</strong></para>
/// <see cref="IHttpCorrelationAccessor"/> is a scoped service. This interceptor is also registered
/// as scoped so that it receives the current request's <see cref="IServiceProvider"/> and resolves
/// the accessor fresh on each <see cref="HandleAsync"/> invocation.
/// </remarks>
internal sealed class HttpCorrelationRequestInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpCorrelationRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve <see cref="IHttpCorrelationAccessor"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public HttpCorrelationRequestInterceptor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Propagates the HTTP correlation ID into the request and invokes the next handler.
    /// </summary>
    /// <param name="request">The request to enrich with a correlation ID.</param>
    /// <param name="handler">The delegate representing the next step in the interceptor chain.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, containing the response.</returns>
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!string.IsNullOrEmpty(request.CorrelationId))
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        var correlationId = _serviceProvider.GetService<IHttpCorrelationAccessor>()?.CorrelationId;
        if (!string.IsNullOrEmpty(correlationId))
        {
            request.CorrelationId = correlationId;
        }

        return await handler(request, cancellationToken).ConfigureAwait(false);
    }
}
