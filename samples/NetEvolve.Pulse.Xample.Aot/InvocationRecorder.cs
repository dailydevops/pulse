namespace NetEvolve.Pulse.Xample.Aot;

using System.Collections.Concurrent;

/// <summary>
/// Collects the handler invocations so the smoke run can assert them.
/// </summary>
internal sealed class InvocationRecorder
{
    private int _activeReservations;
    private int _maxConcurrentReservations;

    public ConcurrentQueue<string> Invocations { get; } = new();

    /// <summary>
    /// Gets the highest number of <see cref="ReserveStockHandler"/> invocations that ran at the same time.
    /// </summary>
    public int MaxConcurrentReservations => Volatile.Read(ref _maxConcurrentReservations);

    public void EnterReservation()
    {
        var active = Interlocked.Increment(ref _activeReservations);
        int max;
        while (active > (max = Volatile.Read(ref _maxConcurrentReservations)))
        {
            _ = Interlocked.CompareExchange(ref _maxConcurrentReservations, active, max);
        }
    }

    public void ExitReservation() => Interlocked.Decrement(ref _activeReservations);
}
