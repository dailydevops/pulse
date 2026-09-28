namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

[PulseHandler]
internal sealed class OrderCreatedHandler(InvocationRecorder recorder) : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue(nameof(OrderCreatedHandler));
        return Task.CompletedTask;
    }
}
