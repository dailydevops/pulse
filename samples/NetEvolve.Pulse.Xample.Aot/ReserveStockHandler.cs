namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

[PulseHandler]
internal sealed class ReserveStockHandler(InvocationRecorder recorder)
    : ICommandHandler<ReserveStockCommand, Extensibility.Void>
{
    public async Task<Extensibility.Void> HandleAsync(
        ReserveStockCommand command,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue(nameof(ReserveStockHandler));
        recorder.EnterReservation();
        try
        {
            // Keeps the handler busy long enough for an unguarded second command to overlap.
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            recorder.ExitReservation();
        }

        return Extensibility.Void.Completed;
    }
}
