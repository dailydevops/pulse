namespace NetEvolve.Pulse.Extensibility;

public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    Task HandleAsync(TEvent message, CancellationToken cancellationToken = default);
}
