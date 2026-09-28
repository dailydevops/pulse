namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

internal sealed record GreetingQuery(string Name) : IQuery<string>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}
