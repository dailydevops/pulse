namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a query that retrieves data and returns a response of type <typeparamref name="TResponse"/>.
/// Queries are intended for read-only operations that don't modify state or trigger side effects.
/// Use <see cref="ICommand{TResponse}"/> for operations that change state.
/// </summary>
/// <typeparam name="TResponse">The type of data returned by the query.</typeparam>
public interface IQuery<TResponse> : IRequest<TResponse>;
