namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Built-in request interceptor that enforces a per-request deadline using a linked
/// <see cref="CancellationTokenSource"/>, without any external dependencies.
/// </summary>
/// <typeparam name="TRequest">The type of request being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Activation:</strong></para>
/// The interceptor enforces a timeout when either:
/// <list type="bullet">
/// <item><description>The request implements <see cref="ITimeoutRequest"/> — its <see cref="ITimeoutRequest.Timeout"/> value is used as the deadline.</description></item>
/// <item><description>A global fallback timeout is configured via <see cref="TimeoutRequestInterceptorOptions.GlobalTimeout"/> — applied to all requests that do not implement <see cref="ITimeoutRequest"/>.</description></item>
/// </list>
/// When neither condition is met the interceptor is a transparent pass-through.
/// <para><strong>Cancellation Semantics:</strong></para>
/// The interceptor correctly distinguishes between a timeout-triggered cancellation and a
/// caller-initiated cancellation: only when the deadline is exceeded is a
/// <see cref="TimeoutException"/> thrown. Caller cancellations are propagated as
/// <see cref="OperationCanceledException"/> as usual.
/// <para><strong>Resource Management:</strong></para>
/// The internally created <see cref="CancellationTokenSource"/> is always disposed, even when
/// the handler throws.
/// </remarks>
internal sealed class TimeoutRequestInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IOptions<TimeoutRequestInterceptorOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="options">The timeout interceptor options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public TimeoutRequestInterceptor(IOptions<TimeoutRequestInterceptorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    /// <exception cref="TimeoutException">
    /// Thrown when the handler does not complete within the configured deadline and the original
    /// <see cref="CancellationToken"/> has not been independently cancelled.
    /// </exception>
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        // Determine the effective timeout for this request.
        // ITimeoutRequest.Timeout takes precedence over the global fallback.
        var timeout = request is ITimeoutRequest timeoutRequest ? timeoutRequest.Timeout : _options.Value.GlobalTimeout;

        // No timeout configured — transparent pass-through.
        if (timeout is null)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout.Value);

        try
        {
            return await handler(request, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && cts.Token.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The request '{typeof(TRequest).Name}' timed out after {timeout.Value.TotalMilliseconds}ms."
            );
        }
    }
}
