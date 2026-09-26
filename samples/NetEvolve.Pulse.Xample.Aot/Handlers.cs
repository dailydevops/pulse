namespace NetEvolve.Pulse.Xample.Aot;

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

[PulseHandler]
internal sealed class CountdownHandler(InvocationRecorder recorder) : IStreamQueryHandler<CountdownStreamQuery, string>
{
    public async IAsyncEnumerable<string> HandleAsync(
        CountdownStreamQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        recorder.Invocations.Enqueue(nameof(CountdownHandler));
        for (var i = request.From; i > 0; i--)
        {
            await Task.Yield();
            yield return i.ToString(CultureInfo.InvariantCulture);
        }
    }
}

[PulseHandler]
internal sealed class RangeHandler : IStreamQueryHandler<RangeStreamQuery, int>
{
    public async IAsyncEnumerable<int> HandleAsync(
        RangeStreamQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        for (var i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

/// <summary>
/// Open-generic request interceptor that records every request passing through the interceptor pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class RecordingRequestInterceptor<TRequest, TResponse>(InvocationRecorder recorder)
    : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Invocations.Enqueue($"RecordingRequestInterceptor<{typeof(TRequest).Name}>");
        return handler(request, cancellationToken);
    }
}

/// <summary>
/// Closed request interceptor for a value-type response, registered next to the built-in open-generic interceptors.
/// </summary>
internal sealed class AddNumbersRecordingInterceptor(InvocationRecorder recorder)
    : IRequestInterceptor<AddNumbersCommand, int>
{
    public Task<int> HandleAsync(
        AddNumbersCommand request,
        Func<AddNumbersCommand, CancellationToken, Task<int>> handler,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Invocations.Enqueue(nameof(AddNumbersRecordingInterceptor));
        return handler(request, cancellationToken);
    }
}

/// <summary>
/// Logger provider that records every log message, so the smoke run can assert that the built-in logging
/// interceptors ran.
/// </summary>
internal sealed class RecordingLoggerProvider(InvocationRecorder recorder) : ILoggerProvider, ILogger
{
    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => recorder.Invocations.Enqueue(formatter(state, exception));

    public void Dispose() { }
}

/// <summary>
/// Collects the handler invocations so the smoke run can assert them.
/// </summary>
internal sealed class InvocationRecorder
{
    private int _activeReservations;
    private int _maxConcurrentReservations;

    public ConcurrentQueue<string> Invocations { get; } = new();

    /// <summary>
    /// Gets the highest number of <see cref="ReserveStockHandler"/> invocations that ran at the same time.
    /// </summary>
    public int MaxConcurrentReservations => Volatile.Read(ref _maxConcurrentReservations);

    public void EnterReservation()
    {
        var active = Interlocked.Increment(ref _activeReservations);
        int max;
        while (active > (max = Volatile.Read(ref _maxConcurrentReservations)))
        {
            _ = Interlocked.CompareExchange(ref _maxConcurrentReservations, active, max);
        }
    }

    public void ExitReservation() => Interlocked.Decrement(ref _activeReservations);
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
internal sealed class ReserveStockHandler(InvocationRecorder recorder)
    : ICommandHandler<ReserveStockCommand, Extensibility.Void>
{
    public async Task<Extensibility.Void> HandleAsync(
        ReserveStockCommand command,
        CancellationToken cancellationToken = default
    )
    {
        recorder.Invocations.Enqueue(nameof(ReserveStockHandler));
        recorder.EnterReservation();
        try
        {
            // Keeps the handler busy long enough for an unguarded second command to overlap.
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            recorder.ExitReservation();
        }

        return Extensibility.Void.Completed;
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
