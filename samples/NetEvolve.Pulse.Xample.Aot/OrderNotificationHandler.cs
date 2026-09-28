namespace NetEvolve.Pulse.Xample.Aot;

using System.Globalization;
using System.Runtime.CompilerServices;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

/// <summary>
/// Second handler for <see cref="OrderCreatedEvent"/> that also handles <see cref="OrderCancelledEvent"/>, so the
/// generated registrations share one instance across both event handler interfaces.
/// </summary>
[PulseHandler]
internal sealed class OrderNotificationHandler(InvocationRecorder recorder)
    : IEventHandler<OrderCreatedEvent>,
        IEventHandler<OrderCancelledEvent>
{
    /// <summary>
    /// Gets the invocation entry that identifies this instance, so the smoke check can prove that both event handler
    /// interfaces resolve the same instance.
    /// </summary>
    private string InstanceMarker =>
        $"{nameof(OrderNotificationHandler)}#{RuntimeHelpers.GetHashCode(this).ToString(CultureInfo.InvariantCulture)}";

    public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue($"{nameof(OrderNotificationHandler)}<{nameof(OrderCreatedEvent)}>");
        recorder.Invocations.Enqueue(InstanceMarker);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderCancelledEvent message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue($"{nameof(OrderNotificationHandler)}<{nameof(OrderCancelledEvent)}>");
        recorder.Invocations.Enqueue(InstanceMarker);
        return Task.CompletedTask;
    }
}
