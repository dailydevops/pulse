namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Request interceptor that enforces exclusive (non-concurrent) execution for commands implementing
/// <see cref="IExclusiveCommand{TResponse}"/> by acquiring a per-command-type
/// <see cref="SemaphoreSlim"/>(1,1) before delegating to the handler.
/// </summary>
/// <typeparam name="TRequest">The type of request being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>If the request does not implement <see cref="IExclusiveCommand{TResponse}"/>, the interceptor passes through with zero overhead.</description></item>
/// <item><description>If the request implements <see cref="IExclusiveCommand{TResponse}"/>, the interceptor acquires a <see cref="SemaphoreSlim"/>(1,1) keyed on the concrete request type before invoking the handler, ensuring at most one concurrent execution per command type.</description></item>
/// <item><description>The semaphore is released in a <see langword="finally"/> block, even if the handler throws.</description></item>
/// </list>
/// <para><strong>Scope:</strong></para>
/// Exclusivity is in-process only. For distributed exclusivity across multiple instances, a distributed lock is required.
/// <para><strong>Registration:</strong></para>
/// Use <c>AddConcurrentCommandGuard()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="IExclusiveCommand{TResponse}"/>
/// <seealso cref="IExclusiveCommand"/>
internal sealed class ConcurrentCommandGuardInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, SemaphoreSlim> _semaphores = new();

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (request is not IExclusiveCommand<TResponse>)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        var semaphore = _semaphores.GetOrAdd(typeof(TRequest), _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = semaphore.Release();
        }
    }
}
