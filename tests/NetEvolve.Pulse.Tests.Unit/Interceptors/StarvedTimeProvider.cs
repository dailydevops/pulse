namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock is advanced manually and whose timers only fire when
/// <see cref="FireTimers"/> is called, modelling a deadline callback that is starved and has not run yet.
/// </summary>
internal sealed class StarvedTimeProvider : TimeProvider
{
    private readonly List<(TimerCallback Callback, object? State)> _timers = [];
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    public void Advance(TimeSpan elapsed) => _ = Interlocked.Add(ref _timestamp, elapsed.Ticks);

    public void FireTimers()
    {
        foreach (var (callback, state) in _timers)
        {
            callback(state);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _timers.Add((callback, state));
        return new ManualTimer();
    }

    private sealed class ManualTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
