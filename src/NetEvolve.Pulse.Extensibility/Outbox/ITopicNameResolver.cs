namespace NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Defines a contract for resolving topic or queue names from outbox messages.
/// </summary>
/// <remarks>
/// Implementations can extract routing destinations from message metadata,
/// typically from <see cref="OutboxMessage.EventType"/>, to support event-driven
/// architectures with topic-based routing (e.g., Dapr pub/sub, Azure Service Bus topics).
/// </remarks>
public interface ITopicNameResolver
{
    /// <summary>
    /// Resolves the topic or queue name for a given outbox message.
    /// </summary>
    /// <param name="message">The outbox message to resolve the topic name from.</param>
    /// <returns>The resolved topic or queue name.</returns>
    string Resolve(OutboxMessage message);
}
