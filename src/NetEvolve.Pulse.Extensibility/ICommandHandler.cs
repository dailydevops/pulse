namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a handler for processing commands of type <typeparamref name="TCommand"/> and producing responses of type <typeparamref name="TResponse"/>.
/// Implementations contain the business logic for executing specific command types.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle, which must implement <see cref="ICommand{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the command handler.</typeparam>
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
