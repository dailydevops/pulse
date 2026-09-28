namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

internal sealed record OrderCancelledEvent(string OrderId) : IEvent
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset? PublishedAt { get; set; }
}
