namespace NetEvolve.Pulse.Extensibility;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Represents a streaming query that retrieves data and returns an asynchronous sequence of items of type <typeparamref name="TResponse"/>.
/// Streaming queries are read-only operations that yield results incrementally without buffering the entire result set in memory.
/// </summary>
/// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
/// <remarks>
/// ⚠️ Streaming query handlers must be side-effect free. Each streaming query type must have exactly one registered handler.
/// Use records for immutable streaming query definitions.
/// </remarks>
/// <example>
/// <code>
/// public record GetAllOrdersStreamQuery(string CustomerId) : IStreamQuery&lt;OrderSummaryDto&gt;;
///
/// public class GetAllOrdersStreamQueryHandler
///     : IStreamQueryHandler&lt;GetAllOrdersStreamQuery, OrderSummaryDto&gt;
/// {
///     private readonly IOrderRepository _repository;
///
///     public async IAsyncEnumerable&lt;OrderSummaryDto&gt; HandleAsync(
///         GetAllOrdersStreamQuery request,
///         [EnumeratorCancellation] CancellationToken cancellationToken)
///     {
///         await foreach (var order in _repository.StreamByCustomerAsync(request.CustomerId, cancellationToken))
///         {
///             yield return new OrderSummaryDto(order.Id, order.Total, order.CreatedAt);
///         }
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IStreamQueryHandler{TQuery, TResponse}"/>
/// <seealso cref="IMediator"/>
[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "As designed.")]
public interface IStreamQuery<TResponse>
{
    /// <summary>
    /// An optional correlation identifier to link related requests and operations across system boundaries.
    /// </summary>
    string? CorrelationId { get; set; }
}
