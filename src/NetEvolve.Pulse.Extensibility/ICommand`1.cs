namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a command that performs an action and returns a response of type <typeparamref name="TResponse"/>.
/// Commands are intended for operations that change state or trigger side effects in the system.
/// Use <see cref="IQuery{TResponse}"/> for read-only operations that don't modify state.
/// </summary>
/// <typeparam name="TResponse">The type of response returned after executing the command.</typeparam>
public interface ICommand<TResponse> : IRequest<TResponse>;
