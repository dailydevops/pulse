namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a handler for processing queries of type <typeparamref name="TQuery"/> and producing responses of type <typeparamref name="TResponse"/>.
/// Implementations contain the business logic for executing specific query types.
/// Query handlers should be side-effect free and idempotent.
/// </summary>
/// <typeparam name="TQuery">The type of query to handle, which must implement <see cref="IQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of data returned by the query handler.</typeparam>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    /// <summary>
    /// Asynchronously handles the specified query and returns the result data.
    /// </summary>
    /// <param name="request">The query to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the query result.</returns>
    Task<TResponse> HandleAsync(TQuery request, CancellationToken cancellationToken = default);
}
