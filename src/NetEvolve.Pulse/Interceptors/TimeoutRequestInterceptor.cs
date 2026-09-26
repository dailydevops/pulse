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
/// The interceptor only activates when the request implements <see cref="ITimeoutRequest"/>.
/// Requests that do not implement <see cref="ITimeoutRequest"/> are always passed through without any timeout.
/// For <see cref="ITimeoutRequest"/> implementations the effective deadline is resolved as follows:
/// <list type="number">
/// <item><description><see cref="ITimeoutRequest.Timeout"/> — used when non-<see langword="null"/>.</description></item>
/// <item><description><see cref="TimeoutRequestInterceptorOptions.GlobalTimeout"/> — used as fallback when <see cref="ITimeoutRequest.Timeout"/> is <see langword="null"/>.</description></item>
/// <item><description>If neither is set, the interceptor is a transparent pass-through for that request.</description></item>
/// </list>
/// <para><strong>Cancellation Semantics:</strong></para>
/// The interceptor correctly distinguishes between a timeout-triggered cancellation and a
/// caller-initiated cancellation: only when the deadline is exceeded is a
/// <see cref="TimeoutException"/> thrown. Caller cancellations are propagated as
/// <see cref="OperationCanceledException"/> as usual.
/// <para><strong>Resource Management:</strong></para>
/// The internally created <see cref="CancellationTokenSource"/> instances are always disposed, even when
/// the handler throws.
/// </remarks>
internal sealed class TimeoutRequestInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IOptions<TimeoutRequestInterceptorOptions> _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutRequestInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="options">The timeout interceptor options.</param>
    /// <param name="timeProvider">The time provider used to schedule and measure the deadline.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> or <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    public TimeoutRequestInterceptor(IOptions<TimeoutRequestInterceptorOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
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

        // Requests not implementing ITimeoutRequest are always passed through.
        if (request is not ITimeoutRequest timeoutRequest)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        // Resolve effective timeout: per-request value first, global fallback second.
        var timeout = timeoutRequest.Timeout ?? _options.Value.GlobalTimeout;

        // No timeout configured — transparent pass-through.
        if (timeout is null)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        using var timeoutCts = new CancellationTokenSource(timeout.Value, _timeProvider);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

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
