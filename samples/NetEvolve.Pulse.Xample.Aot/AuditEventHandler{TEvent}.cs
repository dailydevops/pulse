namespace NetEvolve.Pulse.Xample.Aot;

using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

/// <summary>
/// Open-generic handler registered through <c>[PulseGenericHandler]</c>; the DI container closes it at runtime.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
[PulseGenericHandler]
internal sealed class AuditEventHandler<TEvent>(InvocationRecorder recorder) : IEventHandler<TEvent>
    where TEvent : IEvent
{
    public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue($"AuditEventHandler<{typeof(TEvent).Name}>");
        return Task.CompletedTask;
    }
}
