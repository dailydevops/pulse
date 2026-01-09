namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines an interceptor for commands of type <typeparamref name="TCommand"/> that produce responses of type <typeparamref name="TResponse"/>.
/// Command interceptors allow cross-cutting concerns such as logging, validation, or metrics to be applied to command execution.
/// Multiple interceptors can be chained together to form a pipeline.
/// </summary>
/// <typeparam name="TCommand">The type of command to intercept, which must implement <see cref="ICommand{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the command.</typeparam>
public interface ICommandInterceptor<TCommand, TResponse> : IRequestInterceptor<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;
