namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

internal sealed record RangeStreamQuery(int Count) : IStreamQuery<int>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}
