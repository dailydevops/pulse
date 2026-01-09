namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a base request that produces a response of type <typeparamref name="TResponse"/>.
/// This is the root interface for both commands and queries in the mediator pattern.
/// Typically, you should implement <see cref="ICommand{TResponse}"/> or <see cref="IQuery{TResponse}"/> instead of this interface directly.
/// </summary>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
public interface IRequest<TResponse>;
