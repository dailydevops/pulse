namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

[PulseHandler]
internal sealed class AddNumbersHandler : ICommandHandler<AddNumbersCommand, int>
{
    public Task<int> HandleAsync(AddNumbersCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(command.Left + command.Right);
}
