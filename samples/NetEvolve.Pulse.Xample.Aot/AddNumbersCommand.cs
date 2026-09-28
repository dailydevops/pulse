namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

internal sealed record AddNumbersCommand(int Left, int Right) : ICommand<int>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}
