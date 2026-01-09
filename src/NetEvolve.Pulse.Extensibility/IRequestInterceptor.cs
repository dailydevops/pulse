namespace NetEvolve.Pulse.Extensibility;

public interface IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, Func<TRequest, Task<TResponse>> handler);
}
