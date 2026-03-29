namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility.Outbox;

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
}
