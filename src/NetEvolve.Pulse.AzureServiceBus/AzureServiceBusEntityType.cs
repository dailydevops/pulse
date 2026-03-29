namespace NetEvolve.Pulse;

/// <summary>
/// Defines the Azure Service Bus entity type used for outbox delivery.
/// </summary>
public enum AzureServiceBusEntityType
{
    /// <summary>
    /// Send outbox messages to a queue.
    /// </summary>
    Queue,

    /// <summary>
    /// Send outbox messages to a topic.
    /// </summary>
    Topic,
}
