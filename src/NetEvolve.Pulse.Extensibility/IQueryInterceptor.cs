namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines an interceptor for queries of type <typeparamref name="TQuery"/> that return responses of type <typeparamref name="TResponse"/>.
/// Query interceptors allow cross-cutting concerns such as caching, logging, or authorization to be applied to query execution.
/// Multiple interceptors can be chained together to form a pipeline.
/// </summary>
/// <typeparam name="TQuery">The type of query to intercept, which must implement <see cref="IQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the query.</typeparam>
public interface IQueryInterceptor<TQuery, TResponse> : IRequestInterceptor<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
