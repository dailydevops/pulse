namespace NetEvolve.Pulse.Internals;

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Internal implementation of <see cref="IMediator"/> that coordinates dispatching requests and events to their handlers.
/// This class implements the mediator pattern, providing decoupled communication between components.
/// It supports interceptor pipelines for cross-cutting concerns and configurable event dispatch strategies.
/// </summary>
internal sealed partial class PulseMediator : IMediator
{
    /// <summary>
    /// Logger for capturing errors and diagnostic information.
    /// </summary>
    private readonly ILogger<PulseMediator> _logger;

    /// <summary>
    /// Service provider for resolving handlers and interceptors from the DI container.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Time provider for consistent timestamp generation, supporting testability.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Event dispatcher for controlling how events are dispatched to handlers.
    /// Falls back to parallel dispatch when no custom dispatcher is registered.
    /// </summary>
    private readonly IEventDispatcher _eventDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseMediator"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution.</param>
    /// <param name="timeProvider">The time provider for timestamp generation.</param>
    /// <param name="eventDispatcher">
    /// Optional event dispatcher for custom dispatch strategies. If null, uses <see cref="ParallelEventDispatcher"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown if required parameters are null.</exception>
    public PulseMediator(
        ILogger<PulseMediator> logger,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        IEventDispatcher? eventDispatcher = null
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _logger = logger;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _eventDispatcher = eventDispatcher ?? new ParallelEventDispatcher();
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method executes all registered event handlers using the configured <see cref="IEventDispatcher"/>.
    /// The event's <see cref="IEvent.PublishedAt"/> property is automatically set before handlers execute.
    /// If any handler throws an exception, it is logged but does not prevent other handlers from executing.
    /// Event interceptors are applied in reverse registration order, allowing pre- and post-processing.
    /// </remarks>
    public Task PublishAsync<TEvent>([NotNull] TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        // Set the publication timestamp for tracking purposes
        message.PublishedAt = _timeProvider.GetUtcNow();

        // Retrieve all handlers for the event type
        var handlers = _serviceProvider.GetServices<IEventHandler<TEvent>>();

        // If there are no handlers, simply return
        if (handlers?.Any() != true)
        {
            return Task.CompletedTask;
        }

        // Execute handlers through the interceptor pipeline and dispatcher
        return ExecuteAsync(message, handlers, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method resolves a single query handler from the service provider and executes it through any registered interceptors.
    /// Query interceptors are applied in reverse registration order, forming a pipeline for cross-cutting concerns like caching or logging.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the query type.</exception>
    public Task<TResponse> QueryAsync<TQuery, TResponse>(
        [NotNull] TQuery query,
        CancellationToken cancellationToken = default
    )
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(query);

        // Resolve the appropriate handler for the query
        var handler = _serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();

        return ExecuteAsync(query, q => handler.HandleAsync(q, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method resolves a single command handler from the service provider and executes it through any registered interceptors.
    /// Command interceptors are applied in reverse registration order, forming a pipeline for cross-cutting concerns like validation or auditing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the command type.</exception>
    public Task<TResponse> SendAsync<TCommand, TResponse>(
        [NotNull] TCommand command,
        CancellationToken cancellationToken = default
    )
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(command);

        // Resolve the appropriate handler for the command
        var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();

        return ExecuteAsync(command, c => handler.HandleAsync(c, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Builds and executes an interceptor pipeline for event handling with dispatcher integration.
    /// Interceptors are applied in reverse order of registration, forming a chain where each interceptor
    /// can perform actions before and after calling the next interceptor or final handler.
    /// </summary>
    /// <typeparam name="TEvent">The type of event being processed.</typeparam>
    /// <param name="msg">The event to process.</param>
    /// <param name="handlers">The collection of handlers to dispatch the event to.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous execution of the event through the interceptor pipeline.</returns>
    private Task ExecuteAsync<TEvent>(
        TEvent msg,
        IEnumerable<IEventHandler<TEvent>> handlers,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent
    {
        // Resolve dispatcher: keyed by event type first, then global, then default
        var dispatcher = _serviceProvider.GetKeyedService<IEventDispatcher>(typeof(TEvent)) ?? _eventDispatcher;

        // Create the dispatch action that uses the resolved dispatcher
        Task DispatchAsync(TEvent message) =>
            dispatcher.DispatchAsync(
                message,
                handlers,
                (handler, eventMessage) => InvokeHandlerAsync(handler, eventMessage, cancellationToken),
                cancellationToken
            );

        // Retrieve all registered event interceptors and reverse for correct pipeline order
        var interceptors = _serviceProvider.GetServices<IEventInterceptor<TEvent>>().Reverse().ToArray();
        if (interceptors.Length == 0)
        {
            // No interceptors registered, execute dispatcher directly
            return DispatchAsync(msg);
        }

        // Build the interceptor chain from innermost (dispatcher) to outermost (first interceptor)
        var next = (Func<TEvent, Task>)DispatchAsync;

        foreach (var interceptor in interceptors)
        {
            var currentInterceptor = interceptor;
            var nextCopy = next;
            // Wrap the next action with the current interceptor
            next = req => currentInterceptor.HandleAsync(req, nextCopy, cancellationToken);
        }

        return next(msg);
    }

    /// <summary>
    /// Builds and executes an interceptor pipeline for request handling (commands and queries).
    /// Interceptors are applied in reverse order of registration, forming a chain where each interceptor
    /// can perform actions before and after calling the next interceptor or final handler.
    /// This enables cross-cutting concerns like validation, logging, caching, and metrics without modifying handlers.
    /// </summary>
    /// <typeparam name="TRequest">The type of request being processed.</typeparam>
    /// <typeparam name="TResponse">The type of response produced.</typeparam>
    /// <param name="request">The request to process.</param>
    /// <param name="handler">The final handler to execute after all interceptors.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous execution of the request through the interceptor pipeline.</returns>
    private Task<TResponse> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        Func<TRequest, Task<TResponse>> handler,
        CancellationToken cancellationToken
    )
        where TRequest : IRequest<TResponse>
    {
        // Retrieve all registered request interceptors and reverse for correct pipeline order
        var interceptors = _serviceProvider.GetServices<IRequestInterceptor<TRequest, TResponse>>().Reverse().ToArray();

        if (interceptors.Length == 0)
        {
            // No interceptors registered, execute handler directly
            return handler(request);
        }

        // Build the interceptor chain from innermost (handler) to outermost (first interceptor)
        var next = handler;

        foreach (var interceptor in interceptors)
        {
            var currentInterceptor = interceptor;
            var nextCopy = next;
            // Wrap the next action with the current interceptor
            next = req => currentInterceptor.HandleAsync(req, nextCopy, cancellationToken);
        }

        return next(request);
    }

    private async Task InvokeHandlerAsync<TEvent>(
        IEventHandler<TEvent> handler,
        TEvent message,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent
    {
        try
        {
            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogErrorPublish(message.Id, ex);
            throw;
        }
    }

    /// <summary>
    /// Logs errors that occur during event handler execution.
    /// Uses source-generated logging for optimal performance.
    /// </summary>
    /// <param name="eventId">The ID of the event that caused the error.</param>
    /// <param name="exception">The exception that occurred.</param>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = $"{nameof(PublishAsync)}: Unexpected error occurred. EventId: {{EventId}}",
        SkipEnabledCheck = true
    )]
    private partial void LogErrorPublish(string eventId, Exception exception);
}
