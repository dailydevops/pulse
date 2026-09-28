namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

[PulseHandler]
internal sealed class PingHandler(InvocationRecorder recorder) : ICommandHandler<PingCommand, Extensibility.Void>
{
    public Task<Extensibility.Void> HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue(nameof(PingHandler));
        return Task.FromResult(Extensibility.Void.Completed);
    }
}
