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
/// <para><strong>Deadline Enforcement:</strong></para>
/// Besides cancelling the token passed to the handler, the elapsed time is checked after every
/// step of the enumeration. An item or a completion observed after the deadline results in a
/// <see cref="TimeoutException"/>, even when the deadline callback has not run yet (e.g. under
/// thread-pool starvation) or the handler does not observe the token.
/// <para><strong>Resource Management:</strong></para>
/// The internally created <see cref="CancellationTokenSource"/> instances are always disposed, even when
/// the handler throws.
/// </remarks>
internal sealed class TimeoutStreamQueryInterceptor<TQuery, TResponse> : IStreamQueryInterceptor<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    private readonly IOptions<TimeoutRequestInterceptorOptions> _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutStreamQueryInterceptor{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="options">The timeout interceptor options.</param>
    /// <param name="timeProvider">The time provider used to schedule and measure the deadline.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> or <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    public TimeoutStreamQueryInterceptor(IOptions<TimeoutRequestInterceptorOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
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

        var startTimestamp = _timeProvider.GetTimestamp();
        var timeoutCts = new CancellationTokenSource(timeout.Value, _timeProvider);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

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
                    caughtExceptionInfo = ExceptionDispatchInfo.Capture(CreateTimeoutException(timeout.Value, ex));
                    break;
                }
                catch (Exception ex)
                {
                    caughtExceptionInfo = ExceptionDispatchInfo.Capture(ex);
                    break;
                }

                // The deadline callback may not have run yet (e.g. thread-pool starvation), or the handler
                // may ignore the token: never hand out an item or a completion observed after the deadline.
                if (
                    !cancellationToken.IsCancellationRequested
                    && _timeProvider.GetElapsedTime(startTimestamp) >= timeout.Value
                )
                {
                    caughtExceptionInfo = ExceptionDispatchInfo.Capture(CreateTimeoutException(timeout.Value, null));
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
            timeoutCts.Dispose();
        }

        caughtExceptionInfo?.Throw();
    }

    private static TimeoutException CreateTimeoutException(TimeSpan timeout, Exception? innerException) =>
        new($"The stream query '{typeof(TQuery).Name}' timed out after {timeout.TotalMilliseconds}ms.", innerException);
}
