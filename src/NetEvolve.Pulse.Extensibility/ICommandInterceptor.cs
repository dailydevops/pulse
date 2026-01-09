namespace NetEvolve.Pulse.Extensibility;

public interface ICommandInterceptor<TCommand, TResponse> : IRequestInterceptor<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;
