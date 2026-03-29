namespace NetEvolve.Pulse.Testing;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// A lightweight, thread-safe fake implementation of <see cref="IMediator"/> for unit testing.
/// Supports command and query setup with canned responses or exceptions, event capture,
/// and invocation count verification — all without requiring a DI container.
/// </summary>
/// <example>
/// <code>
/// var mediator = new FakeMediator();
/// mediator.SetupCommand&lt;CreateOrderCommand, OrderResult&gt;()
///     .Returns(new OrderResult { Id = "123" });
///
/// var result = await mediator.SendAsync&lt;CreateOrderCommand, OrderResult&gt;(command);
///
/// mediator.Verify&lt;CreateOrderCommand&gt;(times: 1);
/// </code>
/// </example>
public sealed class FakeMediator : IMediator
{
    private readonly ConcurrentDictionary<Type, object?> _responses = new();
    private readonly ConcurrentDictionary<Type, Exception> _exceptions = new();
    private readonly ConcurrentDictionary<Type, int> _invocationCounts = new();
    private readonly ConcurrentDictionary<Type, ConcurrentQueue<object>> _publishedEvents = new();
    private readonly ConcurrentDictionary<Type, bool> _configuredEvents = new();
    private readonly ConcurrentDictionary<Type, IEnumerable<object>> _streamResponses = new();

    /// <summary>
    /// Configures a canned response or exception for a command of type <typeparamref name="TCommand"/>
    /// returning <typeparamref name="TResponse"/>.
    /// </summary>
    /// <typeparam name="TCommand">The command type to configure.</typeparam>
    /// <typeparam name="TResponse">The response type to return.</typeparam>
    /// <returns>A <see cref="RequestSetup{TResponse}"/> fluent builder for configuring the response or exception.</returns>
    public RequestSetup<TResponse> SetupCommand<TCommand, TResponse>()
        where TCommand : ICommand<TResponse> => new(this, typeof(TCommand));

    /// <summary>
    /// Configures a canned response or exception for a query of type <typeparamref name="TQuery"/>
    /// returning <typeparamref name="TResponse"/>.
    /// </summary>
    /// <typeparam name="TQuery">The query type to configure.</typeparam>
    /// <typeparam name="TResponse">The response type to return.</typeparam>
    /// <returns>A <see cref="RequestSetup{TResponse}"/> fluent builder for configuring the response or exception.</returns>
    public RequestSetup<TResponse> SetupQuery<TQuery, TResponse>()
        where TQuery : IQuery<TResponse> => new(this, typeof(TQuery));

    /// <summary>
    /// Configures canned streaming items or exception for a streaming query of type <typeparamref name="TQuery"/>
    /// yielding items of type <typeparamref name="TResponse"/>.
    /// </summary>
    /// <typeparam name="TQuery">The streaming query type to configure.</typeparam>
    /// <typeparam name="TResponse">The type of each item yielded.</typeparam>
    /// <returns>A <see cref="StreamQuerySetup{TResponse}"/> fluent builder for configuring the items or exception.</returns>
    public StreamQuerySetup<TResponse> SetupStreamQuery<TQuery, TResponse>()
        where TQuery : IStreamQuery<TResponse> => new(this, typeof(TQuery));

    /// <summary>
    /// Registers an event type for capture during <see cref="PublishAsync{TEvent}"/>.
    /// Published events can be retrieved with <see cref="GetPublishedEvents{TEvent}"/>.
    /// </summary>
    /// <typeparam name="TEvent">The event type to capture.</typeparam>
    /// <returns>This <see cref="FakeMediator"/> instance for fluent chaining.</returns>
    public FakeMediator SetupEvent<TEvent>()
        where TEvent : IEvent
    {
        _configuredEvents[typeof(TEvent)] = true;
        _ = _publishedEvents.GetOrAdd(typeof(TEvent), _ => new ConcurrentQueue<object>());
        return this;
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>([NotNull] TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        message.PublishedAt = DateTimeOffset.UtcNow;

        _ = _invocationCounts.AddOrUpdate(typeof(TEvent), 1, (_, count) => count + 1);

        var queue = _publishedEvents.GetOrAdd(typeof(TEvent), _ => new ConcurrentQueue<object>());
        queue.Enqueue(message);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TResponse> QueryAsync<TQuery, TResponse>(
        [NotNull] TQuery query,
        CancellationToken cancellationToken = default
    )
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = typeof(TQuery);
        _ = _invocationCounts.AddOrUpdate(requestType, 1, (_, count) => count + 1);

        if (_exceptions.TryGetValue(requestType, out var exception))
        {
            throw exception;
        }

        if (_responses.TryGetValue(requestType, out var response))
        {
            return Task.FromResult((TResponse)response!);
        }

        throw new InvalidOperationException(
            $"No setup configured for query type '{requestType.Name}'. Call SetupQuery<{requestType.Name}, {typeof(TResponse).Name}>() before querying."
        );
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> StreamQueryAsync<TQuery, TResponse>(
        [NotNull] TQuery query,
        CancellationToken cancellationToken = default
    )
        where TQuery : IStreamQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = typeof(TQuery);
        _ = _invocationCounts.AddOrUpdate(requestType, 1, (_, count) => count + 1);

        if (_exceptions.TryGetValue(requestType, out var exception))
        {
            throw exception;
        }

        if (_streamResponses.TryGetValue(requestType, out var items))
        {
            return StreamItemsAsync<TResponse>(items, cancellationToken);
        }

        throw new InvalidOperationException(
            $"No setup configured for streaming query type '{requestType.Name}'. Call SetupStreamQuery<{requestType.Name}, {typeof(TResponse).Name}>() before querying."
        );
    }

    private static async IAsyncEnumerable<T> StreamItemsAsync<T>(
        IEnumerable<object> items,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return (T)item;
        }
    }

    /// <inheritdoc />
    public Task<TResponse> SendAsync<TCommand, TResponse>(
        [NotNull] TCommand command,
        CancellationToken cancellationToken = default
    )
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = typeof(TCommand);
        _ = _invocationCounts.AddOrUpdate(requestType, 1, (_, count) => count + 1);

        if (_exceptions.TryGetValue(requestType, out var exception))
        {
            throw exception;
        }

        if (_responses.TryGetValue(requestType, out var response))
        {
            return Task.FromResult((TResponse)response!);
        }

        throw new InvalidOperationException(
            $"No setup configured for command type '{requestType.Name}'. Call SetupCommand<{requestType.Name}, {typeof(TResponse).Name}>() before sending."
        );
    }

    /// <summary>
    /// Verifies that a request or event of type <typeparamref name="TRequest"/> was invoked
    /// exactly the specified number of <paramref name="times"/>.
    /// </summary>
    /// <typeparam name="TRequest">The request or event type to verify.</typeparam>
    /// <param name="times">The expected number of invocations.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the actual invocation count does not match <paramref name="times"/>.
    /// </exception>
    public void Verify<TRequest>(int times)
    {
        var requestType = typeof(TRequest);
        _ = _invocationCounts.TryGetValue(requestType, out var actualCount);

        if (actualCount != times)
        {
            throw new InvalidOperationException(
                $"Expected '{requestType.Name}' to be invoked {times} time(s), but was invoked {actualCount} time(s)."
            );
        }
    }

    /// <summary>
    /// Returns all published events of type <typeparamref name="TEvent"/> in publication order.
    /// </summary>
    /// <typeparam name="TEvent">The event type to retrieve.</typeparam>
    /// <returns>A read-only list of published events in the order they were published.</returns>
    public IReadOnlyList<TEvent> GetPublishedEvents<TEvent>()
        where TEvent : IEvent
    {
        if (_publishedEvents.TryGetValue(typeof(TEvent), out var queue))
        {
            return queue.ToArray().Cast<TEvent>().ToList().AsReadOnly();
        }

        return Array.Empty<TEvent>();
    }

    internal void RegisterResponse(Type requestType, object? value)
    {
        _responses[requestType] = value;
        // Clear any previously registered exception for this type
        _ = _exceptions.TryRemove(requestType, out _);
    }

    internal void RegisterStreamResponse(Type requestType, IEnumerable<object> items)
    {
        _streamResponses[requestType] = items;
        // Clear any previously registered exception for this type
        _ = _exceptions.TryRemove(requestType, out _);
    }

    internal void RegisterException(Type requestType, Exception exception)
    {
        _exceptions[requestType] = exception;
        // Clear any previously registered response for this type
        _ = _responses.TryRemove(requestType, out _);
        _ = _streamResponses.TryRemove(requestType, out _);
    }
}
