namespace NetEvolve.Pulse.Extensibility;

public interface IEventInterceptor<TEvent>
    where TEvent : IEvent
{
    Task HandleAsync(TEvent message, Func<TEvent, Task> handler);
}
