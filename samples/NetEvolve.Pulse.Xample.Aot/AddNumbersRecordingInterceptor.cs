namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Closed request interceptor for a value-type response, registered next to the built-in open-generic interceptors.
/// </summary>
internal sealed class AddNumbersRecordingInterceptor(InvocationRecorder recorder)
    : IRequestInterceptor<AddNumbersCommand, int>
{
    public Task<int> HandleAsync(
        AddNumbersCommand request,
        Func<AddNumbersCommand, CancellationToken, Task<int>> handler,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue(nameof(AddNumbersRecordingInterceptor));
        return handler(request, cancellationToken);
    }
}
