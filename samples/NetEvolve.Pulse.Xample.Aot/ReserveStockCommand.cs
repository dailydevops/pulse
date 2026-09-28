namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

internal sealed record ReserveStockCommand(string Sku) : IExclusiveCommand
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}
