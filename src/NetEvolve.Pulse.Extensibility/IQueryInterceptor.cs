namespace NetEvolve.Pulse.Extensibility;

public interface IQueryInterceptor<TQuery, TResponse> : IRequestInterceptor<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
