namespace NetEvolve.Pulse.Internals;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Default <see cref="IRabbitMqChannelPool"/> implementation backed by a
/// <see cref="ConcurrentQueue{T}"/> of idle channels and a <see cref="SemaphoreSlim"/>
/// that caps the number of channels concurrently rented from the pool.
/// </summary>
internal sealed class RabbitMqChannelPool : IRabbitMqChannelPool, IDisposable
{
    private readonly IRabbitMqConnectionAdapter _connectionAdapter;
    private readonly ConcurrentQueue<IRabbitMqChannelAdapter> _idleChannels = new();

    /// <summary>
    /// Caps the number of channels concurrently rented from the pool at
    /// <c>MaxChannelPoolSize</c>. <see cref="RentAsync"/> waits asynchronously when the
    /// pool is exhausted.
    /// </summary>
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed explicitly in Dispose(); the analyzer cannot see the guarded field initialization."
    )]
    private readonly SemaphoreSlim _rentalGate;

    /// <summary>
    /// Disposal sentinel handled via <see cref="Interlocked.Exchange(ref int, int)"/> so
    /// that concurrent <see cref="Dispose"/> calls observe a single winning thread.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqChannelPool"/> class.
    /// </summary>
    /// <param name="connectionAdapter">The RabbitMQ connection adapter used to create new channels.</param>
    /// <param name="maxChannelPoolSize">The maximum number of channels that may be rented concurrently.</param>
    public RabbitMqChannelPool(IRabbitMqConnectionAdapter connectionAdapter, int maxChannelPoolSize)
    {
        ArgumentNullException.ThrowIfNull(connectionAdapter);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxChannelPoolSize, 0);

        _connectionAdapter = connectionAdapter;
        _rentalGate = new SemaphoreSlim(maxChannelPoolSize, maxChannelPoolSize);
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">Thrown when the pool has already been disposed.</exception>
    public async ValueTask<IRabbitMqChannelAdapter> RentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _rentalGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            while (_idleChannels.TryDequeue(out var candidate))
            {
                if (candidate.IsOpen)
                {
                    return candidate;
                }

                // Idle channel closed while waiting in the pool (e.g. broker-side
                // reset); dispose it and try the next one / create a fresh channel.
                candidate.Dispose();
            }

            return await _connectionAdapter.CreateChannelAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Rental slot must always be released exactly once; since Return() will never
            // be called for a channel that failed to be rented, release it here.
            _ = _rentalGate.Release();
            throw;
        }
    }

    /// <inheritdoc />
    public void Return(IRabbitMqChannelAdapter channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Volatile.Read(ref _disposed) == 0 && channel.IsOpen)
        {
            _idleChannels.Enqueue(channel);
        }
        else
        {
            channel.Dispose();
        }

        try
        {
            _ = _rentalGate.Release();
        }
        catch (ObjectDisposedException)
        {
            // The pool was disposed while this channel was rented out; the rental slot
            // no longer matters because the semaphore itself has been torn down.
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="false"/> when the pool has been disposed instead of
    /// throwing, because health probes commonly run during shutdown and should report
    /// unhealthy rather than fail.
    /// </remarks>
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _disposed) != 0)
        {
            return Task.FromResult(false);
        }

        try
        {
            return Task.FromResult(_connectionAdapter.IsOpen);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Disposal is single-shot under concurrency: the first thread to flip
    /// <c>_disposed</c> via <see cref="Interlocked.Exchange(ref int, int)"/> performs the
    /// teardown, disposing every idle channel currently queued and then the semaphore
    /// itself. Channels rented out at the time of disposal are not tracked by the pool and
    /// are therefore not disposed here; their <see cref="Return"/> call disposes them
    /// instead (see the closed-channel branch above).
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        while (_idleChannels.TryDequeue(out var channel))
        {
            channel.Dispose();
        }

        _rentalGate.Dispose();
    }
}
