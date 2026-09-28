namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Open-generic request interceptor that records every request passing through the interceptor pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class RecordingRequestInterceptor<TRequest, TResponse>(InvocationRecorder recorder)
    : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue($"RecordingRequestInterceptor<{typeof(TRequest).Name}>");
        return handler(request, cancellationToken);
    }
}
