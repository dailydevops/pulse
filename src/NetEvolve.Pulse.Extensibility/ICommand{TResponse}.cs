namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a command that performs an action and returns a response of type <typeparamref name="TResponse"/>.
/// Commands are operations that change state or trigger side effects.
/// </summary>
/// <typeparam name="TResponse">The type of response returned after executing the command.</typeparam>
/// <remarks>
/// ⚠️ Each command type must have exactly one registered handler.
/// Use records for immutable command definitions.
/// </remarks>
/// <example>
/// <code>
/// public record CreateCustomerCommand(string Name, string Email) : ICommand&lt;CustomerCreatedResult&gt;;
/// public record CustomerCreatedResult(string CustomerId, DateTime CreatedAt);
///
/// public class CreateCustomerCommandHandler
///     : ICommandHandler&lt;CreateCustomerCommand, CustomerCreatedResult&gt;
/// {
///     private readonly ICustomerRepository _repository;
///
///     public async Task&lt;CustomerCreatedResult&gt; HandleAsync(
///         CreateCustomerCommand command, CancellationToken cancellationToken)
///     {
///         var customer = new Customer { Name = command.Name, Email = command.Email };
///         await _repository.AddAsync(customer, cancellationToken);
///         return new CustomerCreatedResult(customer.Id, customer.CreatedAt);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="ICommand"/>
/// <seealso cref="ICommandHandler{TCommand, TResponse}"/>
public interface ICommand<TResponse> : IRequest<TResponse>;
