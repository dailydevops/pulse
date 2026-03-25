namespace NetEvolve.Pulse;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Configuration options for <see cref="DaprMessageTransport"/>.
/// </summary>
public sealed class DaprMessageTransportOptions
{
    /// <summary>
    /// Gets or sets the name of the Dapr pub/sub component to publish events to.
    /// </summary>
    /// <remarks>Defaults to <c>"pubsub"</c>.</remarks>
    public string PubSubName { get; set; } = "pubsub";

    /// <summary>
    /// Gets or sets the function used to resolve the Dapr topic name from an outbox message.
    /// </summary>
    /// <remarks>
    /// Defaults to extracting the simple class name from <see cref="OutboxMessage.EventType"/>.
    /// For example, <c>"MyApp.Events.OrderCreated, MyApp"</c> resolves to <c>"OrderCreated"</c>.
    /// </remarks>
    public Func<OutboxMessage, string> TopicNameResolver { get; set; } = DefaultTopicNameResolver;

    /// <summary>
    /// Extracts the simple class name from an assembly-qualified type name stored in <see cref="OutboxMessage.EventType"/>.
    /// For example, <c>"MyApp.Events.OrderCreated, MyApp, Version=1.0.0.0, ..."</c> resolves to <c>"OrderCreated"</c>.
    /// </summary>
    /// <param name="message">The outbox message whose <see cref="OutboxMessage.EventType"/> is resolved.</param>
    /// <returns>The simple (unqualified) class name of the event type.</returns>
    internal static string DefaultTopicNameResolver(OutboxMessage message)
    {
        var typeName = message.EventType;
        var commaIndex = typeName.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex > 0)
        {
            typeName = typeName[..commaIndex];
        }

        var dotIndex = typeName.LastIndexOf('.');
        return dotIndex >= 0 ? typeName[(dotIndex + 1)..] : typeName;
    }
}
