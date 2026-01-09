namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a query that retrieves data and returns a response of type <typeparamref name="TResponse"/>.
/// Queries are read-only operations that don't modify state.
/// </summary>
/// <typeparam name="TResponse">The type of data returned by the query.</typeparam>
/// <remarks>
/// ⚠️ Query handlers must be side-effect free. Each query type must have exactly one registered handler.
/// Use records for immutable query definitions.
/// </remarks>
/// <example>
/// <code>
/// public record GetCustomerByIdQuery(string CustomerId) : IQuery&lt;CustomerDetailsDto&gt;;
///
/// public class GetCustomerByIdQueryHandler
///     : IQueryHandler&lt;GetCustomerByIdQuery, CustomerDetailsDto&gt;
/// {
///     private readonly ICustomerRepository _repository;
///
///     public async Task&lt;CustomerDetailsDto&gt; HandleAsync(
///         GetCustomerByIdQuery request, CancellationToken cancellationToken)
///     {
///         var customer = await _repository.GetByIdAsync(request.CustomerId, cancellationToken);
///         return new CustomerDetailsDto(customer.Id, customer.Name, customer.Email);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IQueryHandler{TQuery, TResponse}"/>
/// <seealso cref="IMediator"/>
public interface IQuery<TResponse> : IRequest<TResponse>;
