namespace NetEvolve.Pulse.Extensibility;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Defines a handler for processing streaming queries of type <typeparamref name="TQuery"/> and yielding items of type <typeparamref name="TResponse"/>.
/// Streaming query handlers should be side-effect free and idempotent.
/// </summary>
/// <typeparam name="TQuery">The type of streaming query to handle.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the handler.</typeparam>
/// <remarks>
/// ⚠️ Streaming query handlers must be side-effect free. Each streaming query type must have exactly one registered handler.
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
/// <seealso cref="IStreamQuery{TResponse}" />
/// <seealso cref="IMediator.StreamQueryAsync{TQuery, TResponse}" />
[SuppressMessage(
    "Minor Code Smell",
    "S3246:Generic type parameters should be co/contravariant when possible",
    Justification = "TResponse is used in the type constraint."
)]
public interface IStreamQueryHandler<in TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    /// <summary>
    /// Asynchronously handles the specified streaming query and yields result items.
    /// </summary>
    /// <param name="request">The streaming query to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An async enumerable of items produced by the query.</returns>
    IAsyncEnumerable<TResponse> HandleAsync(TQuery request, CancellationToken cancellationToken = default);
}
