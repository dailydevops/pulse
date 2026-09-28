namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

internal sealed record CreateOrderCommand(string OrderId) : ICommand<OrderResult>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}
