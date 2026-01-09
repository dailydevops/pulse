namespace NetEvolve.Pulse.Extensibility;

public interface IEvent
{
    string Id { get; }

    DateTimeOffset? PublishedAt { get; internal set; }
}
