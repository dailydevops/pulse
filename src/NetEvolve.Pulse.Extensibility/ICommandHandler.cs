namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a handler for processing commands of type <typeparamref name="TCommand"/> and producing responses of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the handler.</typeparam>
/// <remarks>
/// ⚠️ Each command type must have exactly one handler registered as a scoped service.
/// Handlers should manage their own transactions.
/// </remarks>
/// <example>
/// <code>
/// public record UpdateProductPriceCommand(string ProductId, decimal NewPrice)
///     : ICommand&lt;PriceUpdateResult&gt;;
///
/// public class UpdateProductPriceCommandHandler
///     : ICommandHandler&lt;UpdateProductPriceCommand, PriceUpdateResult&gt;
/// {
///     private readonly IProductRepository _repository;
///
///     public async Task&lt;PriceUpdateResult&gt; HandleAsync(
///         UpdateProductPriceCommand command, CancellationToken cancellationToken)
///     {
///         var product = await _repository.GetByIdAsync(command.ProductId, cancellationToken);
///         product.Price = command.NewPrice;
///         await _repository.UpdateAsync(product, cancellationToken);
///         return new PriceUpdateResult(product.Id, product.Price);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="ICommand{TResponse}" />
/// <seealso cref="IMediator.SendAsync{TCommand, TResponse}" />
public interface ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    /// <summary>
    /// Asynchronously handles the specified command and returns a response.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the command response.</returns>
    Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
