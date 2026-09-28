namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

internal sealed record PingCommand : ICommand
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}
