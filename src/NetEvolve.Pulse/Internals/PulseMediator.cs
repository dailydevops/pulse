namespace NetEvolve.Pulse.Internals;

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Internal implementation of <see cref="IMediator"/> that coordinates dispatching requests and events to their handlers.
/// This class implements the mediator pattern, providing decoupled communication between components.
/// It supports interceptor pipelines for cross-cutting concerns and parallel event handler execution.
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
    /// Initializes a new instance of the <see cref="PulseMediator"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution.</param>
    /// <param name="timeProvider">The time provider for timestamp generation.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public PulseMediator(ILogger<PulseMediator> logger, IServiceProvider serviceProvider, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _logger = logger;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method executes all registered event handlers in parallel.
    /// The event's <see cref="IEvent.PublishedAt"/> property is automatically set before handlers execute.
    /// If any handler throws an exception, it is logged but does not prevent other handlers from executing.
    /// Event interceptors are applied in reverse registration order, allowing pre- and post-processing.
    /// </remarks>
    public Task PublishAsync<TEvent>([NotNull] TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        // Retrieve all handlers for the event type
        var handlers = _serviceProvider.GetServices<IEventHandler<TEvent>>();

        // If there are no handlers, simply return
        if (handlers?.Any() != true)
        {
            return Task.CompletedTask;
        }

        // Set the publication timestamp for tracking purposes
        message.PublishedAt = _timeProvider.GetUtcNow();

        // Execute handlers through the interceptor pipeline
        return ExecuteAsync(
            message,
            msg =>
                Parallel.ForEachAsync(
                    handlers,
                    cancellationToken,
                    async (handler, ct) =>
                    {
                        try
                        {
                            await handler.HandleAsync(msg, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // Log handler exceptions but don't fail other handlers
                            LogErrorPublish(message.Id, ex);
                        }
                    }
                )
        );
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

        return ExecuteAsync(query, q => handler.HandleAsync(q, cancellationToken));
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

        return ExecuteAsync(command, c => handler.HandleAsync(c, cancellationToken));
    }

    /// <summary>
    /// Builds and executes an interceptor pipeline for event handling.
    /// Interceptors are applied in reverse order of registration, forming a chain where each interceptor
    /// can perform actions before and after calling the next interceptor or final handler.
    /// </summary>
    /// <typeparam name="TEvent">The type of event being processed.</typeparam>
    /// <param name="msg">The event to process.</param>
    /// <param name="handler">The final handler to execute after all interceptors.</param>
    /// <returns>A task representing the asynchronous execution of the event through the interceptor pipeline.</returns>
    private Task ExecuteAsync<TEvent>(TEvent msg, Func<TEvent, Task> handler)
        where TEvent : IEvent
    {
        // Retrieve all registered event interceptors and reverse for correct pipeline order
        var interceptors = _serviceProvider.GetServices<IEventInterceptor<TEvent>>().Reverse().ToArray();
        if (interceptors.Length == 0)
        {
            // No interceptors registered, execute handler directly
            return handler(msg);
        }

        // Build the interceptor chain from innermost (handler) to outermost (first interceptor)
        var next = handler;

        foreach (var interceptor in interceptors)
        {
            var currentInterceptor = interceptor;
            var nextCopy = next;
            // Wrap the next action with the current interceptor
            next = req => currentInterceptor.HandleAsync(req, nextCopy);
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
    /// <returns>A task representing the asynchronous execution of the request through the interceptor pipeline.</returns>
    private Task<TResponse> ExecuteAsync<TRequest, TResponse>(TRequest request, Func<TRequest, Task<TResponse>> handler)
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
            next = req => currentInterceptor.HandleAsync(req, nextCopy);
        }

        return next(request);
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
