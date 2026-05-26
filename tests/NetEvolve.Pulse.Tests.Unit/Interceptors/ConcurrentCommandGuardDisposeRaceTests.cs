namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

/// <summary>
/// Phase 2 audit verification — Q05 (dispose-race sub-claim).
/// <see cref="ConcurrentCommandGuardInterceptor{TRequest, TResponse}"/> disposes all internally
/// held <see cref="SemaphoreSlim"/> instances in its <c>Dispose</c> method. There is no
/// coordination with in-flight <c>WaitAsync</c> callers, so a parked thread surfaces
/// <see cref="ObjectDisposedException"/> at shutdown instead of <see cref="OperationCanceledException"/>.
///
/// EXPECTED TO FAIL today: the test parks one HandleAsync invocation in <c>WaitAsync</c>,
/// disposes the interceptor on another thread, and asserts that the parked task surfaces
/// <see cref="OperationCanceledException"/>. Today it surfaces <see cref="ObjectDisposedException"/>.
/// </summary>
[TestGroup("Audit-Q05")]
public sealed class ConcurrentCommandGuardDisposeRaceTests
{
    [Test]
    public async Task Dispose_While_HandleAsync_Awaits_Semaphore_Surfaces_OperationCanceled()
    {
        var interceptor = new ConcurrentCommandGuardInterceptor<DisposeRaceCommand, string>();
        var firstHandlerEntered = new TaskCompletionSource();
        var holdFirstHandler = new TaskCompletionSource();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // First call: acquires the semaphore and holds it indefinitely.
        // We intentionally fire-and-forget this task; it will be released later via holdFirstHandler.SetResult().
        _ = Task.Run(
            () =>
                interceptor.HandleAsync(
                    new DisposeRaceCommand(),
                    async (_, ct) =>
                    {
                        firstHandlerEntered.SetResult();
                        await holdFirstHandler.Task.WaitAsync(ct).ConfigureAwait(false);
                        return "first";
                    },
                    cts.Token
                ),
            cts.Token
        );

        await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        // Second call: queues behind the semaphore. This is the task we want to inspect at shutdown.
        var parkedTask = Task.Run(
            () => interceptor.HandleAsync(new DisposeRaceCommand(), (_, _) => Task.FromResult("second"), cts.Token),
            cts.Token
        );

        // Give the parked task a moment to enter SemaphoreSlim.WaitAsync.
        await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token).ConfigureAwait(false);

        // Dispose mid-flight — simulates DI-container shutdown.
        interceptor.Dispose();

        // Release the first handler so it can finish its own work path.
        holdFirstHandler.SetResult();

        Exception? capturedSecondException = null;
        try
        {
            _ = await parkedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            capturedSecondException = ex;
        }

        // ASSERTION CAPTURES THE DEFECT:
        // The shutdown-cancellation contract is OperationCanceledException, not ObjectDisposedException.
        // Today the parked WaitAsync surfaces ObjectDisposedException because Dispose() racing with WaitAsync
        // is not converted into a cooperative cancellation.
        _ = await Assert.That(capturedSecondException).IsNotNull();
        _ = await Assert.That(capturedSecondException).IsTypeOf<OperationCanceledException>();
    }

    private sealed record DisposeRaceCommand : IExclusiveCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
