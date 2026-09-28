namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

internal sealed record CountdownStreamQuery(int From) : IStreamQuery<string>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}
