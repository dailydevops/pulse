namespace NetEvolve.Pulse.Extensibility;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<TResponse> HandleAsync(TQuery request, CancellationToken cancellationToken = default);
}
