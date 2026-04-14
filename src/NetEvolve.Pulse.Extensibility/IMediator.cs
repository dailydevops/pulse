namespace NetEvolve.Pulse.Extensibility;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines the mediator pattern implementation for decoupling request/response and event publishing.
/// Provides a central point for dispatching commands, queries, and events to their handlers.
/// </summary>
/// <remarks>
/// ⚠️ Commands and queries require exactly one handler. Events can have zero or more handlers.
/// Thread-safe when registered as scoped service.
/// <para>
/// For write-side services that must never call read operations, inject <see cref="IMediatorSendOnly"/> instead.
/// </para>
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
/// <seealso cref="IMediatorSendOnly"/>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="IQuery{TResponse}"/>
/// <seealso cref="IEvent"/>
public interface IMediator : IMediatorSendOnly
{
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
    /// Executes a streaming query and returns an asynchronous sequence of items.
    /// Streaming queries are read-only operations that yield results incrementally without buffering the entire result set in memory.
    /// </summary>
    /// <typeparam name="TQuery">The type of streaming query to execute.</typeparam>
    /// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
    /// <param name="query">The streaming query to execute.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An asynchronous sequence of result items.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the streaming query type.</exception>
    /// <remarks>
    /// ⚠️ Exactly one handler must be registered for each streaming query type.
    /// Items are yielded incrementally; the caller must enumerate the result to trigger execution.
    /// </remarks>
    IAsyncEnumerable<TResponse> StreamQueryAsync<TQuery, TResponse>(
        [NotNull] TQuery query,
        CancellationToken cancellationToken = default
    )
        where TQuery : IStreamQuery<TResponse>;
}
