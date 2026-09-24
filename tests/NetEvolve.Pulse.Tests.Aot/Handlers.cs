namespace NetEvolve.Pulse.Tests.Aot;

using System.Collections.Concurrent;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

/// <summary>
/// Collects the handler invocations so the smoke run can assert them.
/// </summary>
internal sealed class InvocationRecorder
{
    public ConcurrentQueue<string> Invocations { get; } = new();
}

[PulseHandler]
internal sealed class CreateOrderHandler(InvocationRecorder recorder) : ICommandHandler<CreateOrderCommand, OrderResult>
{
    public Task<OrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        recorder.Invocations.Enqueue(nameof(CreateOrderHandler));
        return Task.FromResult(new OrderResult(command.OrderId, Accepted: true));
    }
}

[PulseHandler]
internal sealed class AddNumbersHandler : ICommandHandler<AddNumbersCommand, int>
{
    public Task<int> HandleAsync(AddNumbersCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(command.Left + command.Right);
}

[PulseHandler]
internal sealed class PingHandler(InvocationRecorder recorder) : ICommandHandler<PingCommand, Extensibility.Void>
{
    public Task<Extensibility.Void> HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        recorder.Invocations.Enqueue(nameof(PingHandler));
        return Task.FromResult(Extensibility.Void.Completed);
    }
}

[PulseHandler]
internal sealed class GreetingHandler : IQueryHandler<GreetingQuery, string>
{
    public Task<string> HandleAsync(GreetingQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult($"Hello, {query.Name}!");
}

[PulseHandler]
internal sealed class OrderCreatedHandler(InvocationRecorder recorder) : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        recorder.Invocations.Enqueue(nameof(OrderCreatedHandler));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Open-generic handler registered through <c>[PulseGenericHandler]</c>; the DI container closes it at runtime.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
[PulseGenericHandler]
internal sealed class AuditEventHandler<TEvent>(InvocationRecorder recorder) : IEventHandler<TEvent>
    where TEvent : IEvent
{
    public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default)
    {
        recorder.Invocations.Enqueue($"AuditEventHandler<{typeof(TEvent).Name}>");
        return Task.CompletedTask;
    }
}
