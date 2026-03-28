namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Http.Correlation.Abstractions;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event interceptor that propagates the HTTP correlation ID from <see cref="IHttpCorrelationAccessor"/>
/// into every <see cref="IEvent"/> dispatched through the mediator.
/// </summary>
/// <typeparam name="TEvent">The type of event to intercept, which must implement <see cref="IEvent"/>.</typeparam>
/// <remarks>
/// <para><strong>Propagation Logic:</strong></para>
/// Before invoking the next handler delegate, the interceptor sets <c>message.CorrelationId</c> from
/// <see cref="IHttpCorrelationAccessor.CorrelationId"/> only when both of the following conditions hold:
/// <list type="bullet">
/// <item><description><c>message.CorrelationId</c> is <see langword="null"/> or empty.</description></item>
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
internal sealed class HttpCorrelationEventInterceptor<TEvent> : IEventInterceptor<TEvent>
    where TEvent : IEvent
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpCorrelationEventInterceptor{TEvent}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve <see cref="IHttpCorrelationAccessor"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public HttpCorrelationEventInterceptor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Propagates the HTTP correlation ID into the event message and invokes the next handler.
    /// </summary>
    /// <param name="message">The event message to enrich with a correlation ID.</param>
    /// <param name="handler">The delegate representing the next step in the interceptor chain.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task HandleAsync(
        TEvent message,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!string.IsNullOrEmpty(message.CorrelationId))
        {
            await handler(message, cancellationToken).ConfigureAwait(false);
            return;
        }

        var correlationId = _serviceProvider.GetService<IHttpCorrelationAccessor>()?.CorrelationId;
        if (!string.IsNullOrEmpty(correlationId))
        {
            message.CorrelationId = correlationId;
        }

        await handler(message, cancellationToken).ConfigureAwait(false);
    }
}
