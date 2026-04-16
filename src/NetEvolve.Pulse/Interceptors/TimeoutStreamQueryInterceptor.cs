namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Built-in stream query interceptor that enforces a per-request deadline using a linked
/// <see cref="CancellationTokenSource"/>, without any external dependencies.
/// </summary>
/// <typeparam name="TQuery">The type of stream query being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the stream query.</typeparam>
/// <remarks>
/// <para><strong>Activation:</strong></para>
/// The interceptor only activates when the query implements <see cref="ITimeoutRequest"/>.
/// Queries that do not implement <see cref="ITimeoutRequest"/> are always passed through without any timeout.
/// For <see cref="ITimeoutRequest"/> implementations the effective deadline is resolved as follows:
/// <list type="number">
/// <item><description><see cref="ITimeoutRequest.Timeout"/> — used when non-<see langword="null"/>.</description></item>
/// <item><description><see cref="TimeoutRequestInterceptorOptions.GlobalTimeout"/> — used as fallback when <see cref="ITimeoutRequest.Timeout"/> is <see langword="null"/>.</description></item>
/// <item><description>If neither is set, the interceptor is a transparent pass-through for that query.</description></item>
/// </list>
/// <para><strong>Cancellation Semantics:</strong></para>
/// The interceptor correctly distinguishes between a timeout-triggered cancellation and a
/// caller-initiated cancellation: only when the deadline is exceeded is a
/// <see cref="TimeoutException"/> thrown. Caller cancellations are propagated as
/// <see cref="OperationCanceledException"/> as usual.
/// <para><strong>Resource Management:</strong></para>
/// The internally created <see cref="CancellationTokenSource"/> is always disposed, even when
/// the handler throws.
/// </remarks>
internal sealed class TimeoutStreamQueryInterceptor<TQuery, TResponse> : IStreamQueryInterceptor<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    private readonly IOptions<TimeoutRequestInterceptorOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutStreamQueryInterceptor{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="options">The timeout interceptor options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public TimeoutStreamQueryInterceptor(IOptions<TimeoutRequestInterceptorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    /// <exception cref="TimeoutException">
    /// Thrown when the stream enumeration does not complete within the configured deadline and the original
    /// <see cref="CancellationToken"/> has not been independently cancelled.
    /// </exception>
    public IAsyncEnumerable<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        return HandleCoreAsync(request, handler, cancellationToken);
    }

    private async IAsyncEnumerable<TResponse> HandleCoreAsync(
        TQuery request,
        Func<TQuery, CancellationToken, IAsyncEnumerable<TResponse>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        // Queries not implementing ITimeoutRequest are always passed through.
        if (request is not ITimeoutRequest timeoutRequest)
        {
            await foreach (
                var item in handler(request, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                yield return item;
            }

            yield break;
        }

        // Resolve effective timeout: per-request value first, global fallback second.
        var timeout = timeoutRequest.Timeout ?? _options.Value.GlobalTimeout;

        // No timeout configured — transparent pass-through.
        if (timeout is null)
        {
            await foreach (
                var item in handler(request, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                yield return item;
            }

            yield break;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout.Value);

        // yield return is not allowed inside a try/catch block, so we capture any exception
        // from the inner enumerator and re-throw it after the yield loop completes.
        ExceptionDispatchInfo? caughtExceptionInfo = null;

        var linkedToken = cts.Token;
        var enumerator = handler(request, linkedToken).GetAsyncEnumerator(linkedToken);
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                    when (!cancellationToken.IsCancellationRequested && linkedToken.IsCancellationRequested)
                {
                    caughtExceptionInfo = ExceptionDispatchInfo.Capture(
                        new TimeoutException(
                            $"The stream query '{typeof(TQuery).Name}' timed out after {timeout.Value.TotalMilliseconds}ms.",
                            ex
                        )
                    );
                    break;
                }
                catch (Exception ex)
                {
                    caughtExceptionInfo = ExceptionDispatchInfo.Capture(ex);
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                // yield return is valid here: it is inside try/finally but NOT inside try/catch
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        caughtExceptionInfo?.Throw();
    }
}
