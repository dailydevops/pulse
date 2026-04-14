namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Http.Correlation.Abstractions;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Stream query interceptor that propagates the HTTP correlation ID from <see cref="IHttpCorrelationAccessor"/>
/// into every <see cref="IStreamQuery{TResponse}"/> dispatched through the mediator.
/// </summary>
/// <typeparam name="TQuery">The type of streaming query to intercept, which must implement <see cref="IStreamQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
/// <remarks>
/// <para><strong>Propagation Logic:</strong></para>
/// Before yielding the first item, the interceptor sets <c>request.CorrelationId</c> from
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
internal sealed class HttpCorrelationStreamQueryInterceptor<TQuery, TResponse>
    : IStreamQueryInterceptor<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpCorrelationStreamQueryInterceptor{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve <see cref="IHttpCorrelationAccessor"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public HttpCorrelationStreamQueryInterceptor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Propagates the HTTP correlation ID into the stream query request and yields all items from the handler.
    /// </summary>
    /// <param name="request">The stream query to enrich with a correlation ID.</param>
    /// <param name="handler">The delegate representing the next step in the interceptor chain.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An asynchronous sequence of result items.</returns>
    public IAsyncEnumerable<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (string.IsNullOrEmpty(request.CorrelationId))
        {
            var correlationId = _serviceProvider.GetService<IHttpCorrelationAccessor>()?.CorrelationId;
            if (!string.IsNullOrEmpty(correlationId))
            {
                request.CorrelationId = correlationId;
            }
        }

        return IterateAsync(request, handler, cancellationToken);
    }

    private static async IAsyncEnumerable<TResponse> IterateAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (
            var item in handler(request, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            yield return item;
        }
    }
}
