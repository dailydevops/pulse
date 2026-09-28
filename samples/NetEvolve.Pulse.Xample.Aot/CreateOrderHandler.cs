namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

[PulseHandler]
internal sealed class CreateOrderHandler(InvocationRecorder recorder) : ICommandHandler<CreateOrderCommand, OrderResult>
{
    public Task<OrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue(nameof(CreateOrderHandler));
        return Task.FromResult(new OrderResult(command.OrderId, Accepted: true));
    }
}
