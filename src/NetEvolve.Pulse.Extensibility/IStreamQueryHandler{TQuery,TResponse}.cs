namespace NetEvolve.Pulse.Extensibility;

using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Defines a handler for processing streaming queries of type <typeparamref name="TQuery"/> and yielding responses of type <typeparamref name="TResponse"/>.
/// Streaming query handlers should be side-effect free and return results incrementally via <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
/// <typeparam name="TQuery">The type of streaming query to handle.</typeparam>
/// <typeparam name="TResponse">The type of each item yielded by the handler.</typeparam>
/// <remarks>
/// ⚠️ Streaming query handlers must be side-effect free. Each streaming query type must have exactly one registered handler.
/// </remarks>
/// <example>
/// <code>
/// public record GetAllProductsStreamQuery(string CategoryId) : IStreamQuery&lt;ProductDto&gt;;
///
/// public class GetAllProductsStreamQueryHandler
///     : IStreamQueryHandler&lt;GetAllProductsStreamQuery, ProductDto&gt;
/// {
///     private readonly IProductRepository _repository;
///
///     public async IAsyncEnumerable&lt;ProductDto&gt; HandleAsync(
///         GetAllProductsStreamQuery request,
///         [EnumeratorCancellation] CancellationToken cancellationToken)
///     {
///         await foreach (var product in _repository.StreamByCategoryAsync(request.CategoryId, cancellationToken))
///         {
///             yield return new ProductDto(product.Id, product.Name, product.Price);
///         }
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IStreamQuery{TResponse}" />
/// <seealso cref="IMediator"/>
public interface IStreamQueryHandler<in TQuery, out TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    /// <summary>
    /// Asynchronously handles the specified streaming query and yields the result items.
    /// </summary>
    /// <param name="request">The streaming query to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An asynchronous sequence of result items.</returns>
    IAsyncEnumerable<TResponse> HandleAsync(TQuery request, CancellationToken cancellationToken = default);
}
