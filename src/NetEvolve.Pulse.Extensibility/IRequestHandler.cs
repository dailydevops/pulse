namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a handler for processing queries of type <typeparamref name="TQuery"/> and producing responses of type <typeparamref name="TResponse"/>.
/// Query handlers should be side-effect free and idempotent.
/// </summary>
/// <typeparam name="TQuery">The type of query to handle.</typeparam>
/// <typeparam name="TResponse">The type of data returned by the handler.</typeparam>
/// <remarks>
/// ⚠️ Query handlers must be side-effect free. Each query type must have exactly one registered handler.
/// </remarks>
/// <example>
/// <code>
/// public record GetUserProfileQuery(string UserId) : IQuery&lt;UserProfileDto&gt;;
///
/// public class GetUserProfileQueryHandler
///     : IQueryHandler&lt;GetUserProfileQuery, UserProfileDto&gt;
/// {
///     private readonly IUserRepository _repository;
///
///     public async Task&lt;UserProfileDto&gt; HandleAsync(
///         GetUserProfileQuery request, CancellationToken cancellationToken)
///     {
///         var user = await _repository.GetByIdAsync(request.UserId, cancellationToken);
///         return new UserProfileDto(user.Id, user.Name, user.Email);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IQuery{TResponse}" />
/// <seealso cref="IMediator.QueryAsync{TQuery, TResponse}" />
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
