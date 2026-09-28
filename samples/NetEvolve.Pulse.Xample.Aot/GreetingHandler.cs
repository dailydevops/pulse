namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

[PulseHandler]
internal sealed class GreetingHandler : IQueryHandler<GreetingQuery, string>
{
    public Task<string> HandleAsync(GreetingQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult($"Hello, {query.Name}!");
}
