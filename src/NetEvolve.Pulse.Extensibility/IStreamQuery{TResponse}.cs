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
    /// Gets or sets the causation identifier that records which command or event directly caused this stream query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set <c>CausationId</c> to the <c>Id</c> of the command or event that directly triggered the current stream query.
    /// Combined with <see cref="CorrelationId"/>, this enables full causal chain reconstruction.
    /// The mediator does <strong>not</strong> populate this value automatically — the caller is responsible.
    /// </para>
    /// <para><strong>Example causal chain:</strong></para>
    /// <code>
    /// PlaceOrder      (Command, Id: "cmd-1",  CorrelationId: "txn-42")
    ///   └─► OrderPlaced (Event,  Id: "evt-1",  CorrelationId: "txn-42", CausationId: "cmd-1")
    ///         └─► ReserveInventory (Command, Id: "cmd-2", CorrelationId: "txn-42", CausationId: "evt-1")
    ///               └─► InventoryReserved (Event, Id: "evt-2", CorrelationId: "txn-42", CausationId: "cmd-2")
    /// </code>
    /// </remarks>
    string? CausationId { get; set; }

    /// <summary>
    /// An optional correlation identifier to link related requests and operations across system boundaries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CorrelationId</c> groups all events and commands that belong to the same logical transaction or workflow.
    /// Use <see cref="CausationId"/> when you also need to reconstruct the exact cause-effect chain within that group.
    /// </para>
    /// </remarks>
    string? CorrelationId { get; set; }
}
