namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event type that the application only publishes and never names through <see langword="typeof"/>, like an outbox
/// event that is rehydrated from its persisted type name.
/// </summary>
internal sealed record ShipmentDispatchedEvent(string ShipmentId) : IEvent
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset? PublishedAt { get; set; }
}
