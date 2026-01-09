namespace NetEvolve.Pulse.Extensibility;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Defines the mediator pattern implementation for decoupling request/response and event publishing in the application.
/// The mediator provides a central point for dispatching commands, queries, and events to their respective handlers.
/// This promotes loose coupling and enables cross-cutting concerns through interceptors.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Asynchronously publishes an event to all registered handlers of type <see cref="IEventHandler{TEvent}"/>.
    /// All handlers are executed in parallel, and exceptions in individual handlers are logged but don't prevent other handlers from executing.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to publish, which must implement <see cref="IEvent"/>.</typeparam>
    /// <param name="message">The event to publish. Cannot be null.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishAsync<TEvent>([NotNull] TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// Asynchronously executes a query and returns the result.
    /// Queries are intended for read-only operations that don't modify state.
    /// </summary>
    /// <typeparam name="TQuery">The type of query to execute, which must implement <see cref="IQuery{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The type of response returned by the query.</typeparam>
    /// <param name="query">The query to execute. Cannot be null.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the query result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the query type.</exception>
    Task<TResponse> QueryAsync<TQuery, TResponse>([NotNull] TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResponse>;

    /// <summary>
    /// Asynchronously sends a command for execution and returns the result.
    /// Commands are intended for operations that change state or trigger side effects.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute, which must implement <see cref="ICommand{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The type of response returned by the command.</typeparam>
    /// <param name="command">The command to execute. Cannot be null.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the command result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the command type.</exception>
    Task<TResponse> SendAsync<TCommand, TResponse>(
        [NotNull] TCommand command,
        CancellationToken cancellationToken = default
    )
        where TCommand : ICommand<TResponse>;

    /// <summary>
    /// Asynchronously sends a command for execution without expecting a meaningful return value.
    /// This is a convenience method for commands that return <see cref="Void"/>.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute, which must implement <see cref="ICommand"/>.</typeparam>
    /// <param name="command">The command to execute. Cannot be null.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the command type.</exception>
    Task SendAsync<TCommand>([NotNull] TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand => SendAsync<TCommand, Void>(command, cancellationToken);
}
