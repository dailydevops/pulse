namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines an interceptor for commands of type <typeparamref name="TCommand"/> that produce responses of type <typeparamref name="TResponse"/>.
/// Enables cross-cutting concerns like logging, validation, or transaction management for commands.
/// </summary>
/// <typeparam name="TCommand">The type of command to intercept.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the command.</typeparam>
/// <remarks>
/// This interface extends <see cref="IRequestInterceptor{TRequest, TResponse}"/>.
/// See <see cref="IRequestInterceptor{TRequest, TResponse}"/> for implementation details.
/// </remarks>
/// <seealso cref="ICommand{TResponse}" />
/// <seealso cref="IRequestInterceptor{TRequest, TResponse}" />
public interface ICommandInterceptor<TCommand, TResponse> : IRequestInterceptor<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;
