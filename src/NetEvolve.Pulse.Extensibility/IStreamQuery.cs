namespace NetEvolve.Pulse.Extensibility;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Represents a streaming query that retrieves data incrementally and returns items of type <typeparamref name="TResponse"/> via <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/>.
/// Streaming queries are read-only operations that don't modify state and allow processing results as they arrive.
/// </summary>
/// <typeparam name="TResponse">The type of each item yielded by the streaming query.</typeparam>
/// <remarks>
/// ⚠️ Streaming query handlers must be side-effect free. Each streaming query type must have exactly one registered handler.
/// Use records for immutable streaming query definitions.
/// Use this interface for large result sets, paginated exports, report generation, or real-time feeds where
/// buffering the entire result set in memory would be problematic.
/// </remarks>
/// <example>
/// <code>
/// public record GetProductsStreamQuery(string CategoryId) : IStreamQuery&lt;ProductDto&gt;;
///
/// public class GetProductsStreamQueryHandler
///     : IStreamQueryHandler&lt;GetProductsStreamQuery, ProductDto&gt;
/// {
///     private readonly IProductRepository _repository;
///
///     public async IAsyncEnumerable&lt;ProductDto&gt; HandleAsync(
///         GetProductsStreamQuery request,
///         [EnumeratorCancellation] CancellationToken cancellationToken = default)
///     {
///         await foreach (var product in _repository.GetByCategoryAsync(request.CategoryId, cancellationToken))
///         {
///             yield return new ProductDto(product.Id, product.Name, product.Price);
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
