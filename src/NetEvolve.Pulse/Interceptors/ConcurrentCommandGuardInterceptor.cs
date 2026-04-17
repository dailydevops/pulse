namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Command interceptor that enforces exclusive (non-concurrent) execution for commands implementing
/// <see cref="IExclusiveCommand{TResponse}"/> by acquiring a per-command-type
/// <see cref="SemaphoreSlim"/>(1,1) before delegating to the handler.
/// </summary>
/// <typeparam name="TRequest">
/// The type of command being intercepted. Must implement <see cref="IExclusiveCommand{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">The type of response produced by the command.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>The interceptor acquires a <see cref="SemaphoreSlim"/>(1,1) keyed on the concrete request type before invoking the handler, ensuring at most one concurrent execution per command type.</description></item>
/// <item><description>The semaphore is released in a <see langword="finally"/> block, even if the handler throws.</description></item>
/// </list>
/// <para><strong>Scope:</strong></para>
/// Exclusivity is in-process only. For distributed exclusivity across multiple instances, a distributed lock is required.
/// <para><strong>Memory:</strong></para>
/// One <see cref="SemaphoreSlim"/> instance is created per distinct command type and held in an instance-level
/// dictionary for the lifetime of the interceptor. When registered as a singleton (the typical case), this
/// equals the application lifetime. For a bounded set of command types this is acceptable, but applications
/// that dynamically generate many unique command types should be aware of this retention.
/// <para><strong>Disposal:</strong></para>
/// Implements <see cref="IDisposable"/> to release all internally held <see cref="SemaphoreSlim"/> instances.
/// When registered as a singleton via <c>AddConcurrentCommandGuard&lt;TRequest, TResponse&gt;()</c>, disposal
/// is managed by the DI container at application shutdown.
/// <para><strong>Registration:</strong></para>
/// Use <c>AddConcurrentCommandGuard()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="IExclusiveCommand{TResponse}"/>
/// <seealso cref="IExclusiveCommand"/>
internal sealed class ConcurrentCommandGuardInterceptor<TRequest, TResponse>
    : ICommandInterceptor<TRequest, TResponse>,
        IDisposable
    where TRequest : IExclusiveCommand<TResponse>
{
    private readonly ConcurrentDictionary<Type, SemaphoreSlim> _semaphores = new();
    private bool _disposedValue;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

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

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // Dispose managed state (managed objects)
                foreach (var semaphore in _semaphores.Values)
                {
                    semaphore.Dispose();
                }
                _semaphores.Clear();
            }

            _disposedValue = true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
