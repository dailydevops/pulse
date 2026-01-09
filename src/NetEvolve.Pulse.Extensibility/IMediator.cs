namespace NetEvolve.Pulse.Extensibility;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Defines the mediator pattern implementation for decoupling request/response and event publishing.
/// Provides a central point for dispatching commands, queries, and events to their handlers.
/// </summary>
/// <remarks>
/// ⚠️ Commands and queries require exactly one handler. Events can have zero or more handlers.
/// Thread-safe when registered as scoped service.
/// </remarks>
/// <example>
/// <code>
/// public class OrderService
/// {
///     private readonly IMediator _mediator;
///
///     public async Task&lt;OrderResult&gt; CreateOrder(CreateOrderRequest request)
///     {
///         var command = new CreateOrderCommand(request.Items, request.CustomerId);
///         var result = await _mediator.SendAsync&lt;CreateOrderCommand, OrderResult&gt;(command);
///         await _mediator.PublishAsync(new OrderCreatedEvent { OrderId = result.OrderId });
///         return result;
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="IQuery{TResponse}"/>
/// <seealso cref="IEvent"/>
public interface IMediator
{
    /// <summary>
    /// Asynchronously publishes an event to all registered handlers.
    /// All handlers execute in parallel, and exceptions in individual handlers don't prevent others from executing.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to publish.</typeparam>
    /// <param name="message">The event to publish.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// ⚠️ Event handlers should be idempotent. The mediator automatically sets <see cref="IEvent.PublishedAt"/>.
    /// </remarks>
    Task PublishAsync<TEvent>([NotNull] TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// Asynchronously executes a query and returns the result. Queries are read-only operations.
    /// </summary>
    /// <typeparam name="TQuery">The type of query to execute.</typeparam>
    /// <typeparam name="TResponse">The type of response returned by the query.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The query result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the query type.</exception>
    /// <remarks>
    /// ⚠️ Exactly one handler must be registered for each query type.
    /// </remarks>
    Task<TResponse> QueryAsync<TQuery, TResponse>([NotNull] TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResponse>;

    /// <summary>
    /// Asynchronously sends a command for execution and returns the result.
    /// Commands are operations that change state or trigger side effects.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute.</typeparam>
    /// <typeparam name="TResponse">The type of response returned by the command.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The command result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the command type.</exception>
    /// <remarks>
    /// ⚠️ Exactly one handler must be registered for each command type.
    /// </remarks>
    Task<TResponse> SendAsync<TCommand, TResponse>(
        [NotNull] TCommand command,
        CancellationToken cancellationToken = default
    )
        where TCommand : ICommand<TResponse>;

    /// <summary>
    /// Asynchronously sends a command for execution without a return value.
    /// Convenience method for commands that return <see cref="Void"/>.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the command type.</exception>
    Task SendAsync<TCommand>([NotNull] TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand => SendAsync<TCommand, Void>(command, cancellationToken);
}
