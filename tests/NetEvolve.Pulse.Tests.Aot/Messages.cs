namespace NetEvolve.Pulse.Tests.Aot;

using NetEvolve.Pulse.Extensibility;

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
