namespace NetEvolve.Pulse.Xample.Aot;

using System.Text.Json.Serialization;
using NetEvolve.Pulse.Extensibility;

internal sealed record CountdownStreamQuery(int From) : IStreamQuery<string>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}

/// <summary>
/// Event type that the application only publishes and never names through <c>typeof</c>, like an outbox event
/// that is rehydrated from its persisted type name.
/// </summary>
internal sealed record ShipmentDispatchedEvent(string ShipmentId) : IEvent
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// Payload type that is intentionally missing from <see cref="SmokeJsonContext"/>.
/// </summary>
internal sealed record UnregisteredPayload(int Value);

/// <summary>
/// Source-generated JSON contracts for the payloads of the smoke application.
/// </summary>
[JsonSerializable(typeof(OrderCreatedEvent))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;

internal sealed record CreateOrderCommand(string OrderId) : ICommand<OrderResult>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}

internal sealed record OrderResult(string OrderId, bool Accepted);

internal sealed record AddNumbersCommand(int Left, int Right) : ICommand<int>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}

internal sealed record PingCommand : ICommand
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}

internal sealed record GreetingQuery(string Name) : IQuery<string>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}

internal sealed record OrderCreatedEvent(string OrderId) : IEvent
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset? PublishedAt { get; set; }
}
