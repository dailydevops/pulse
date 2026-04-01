namespace NetEvolve.Pulse;

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods on <see cref="IMediatorBuilder" /> for registering the Apache Kafka
/// outbox transport.
/// </summary>
public static class KafkaExtensions
{
    /// <summary>
    /// Registers the Kafka outbox transport so that outbox messages are produced to Kafka topics.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <returns>The same <paramref name="configurator" /> instance for chaining.</returns>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// <c>IProducer&lt;string, string&gt;</c> and <c>IAdminClient</c> from <c>Confluent.Kafka</c>
    /// must be registered in the DI container by the caller before this method is invoked, for
    /// example:
    /// <code>
    /// services.AddSingleton&lt;IProducer&lt;string, string&gt;&gt;(sp =>
    ///     new ProducerBuilder&lt;string, string&gt;(
    ///         new ProducerConfig { BootstrapServers = "localhost:9092", Acks = Acks.All })
    ///     .Build());
    /// services.AddSingleton&lt;IAdminClient&gt;(sp =>
    ///     new AdminClientBuilder(
    ///         new AdminClientConfig { BootstrapServers = "localhost:9092" })
    ///     .Build());
    /// </code>
    /// <para><strong>Topic routing:</strong></para>
    /// The destination topic for each message is resolved by the registered
    /// <see cref="ITopicNameResolver" />. The default implementation uses the simple class name
    /// extracted from <see cref="OutboxMessage.EventType" />. Register a custom
    /// <see cref="ITopicNameResolver" /> before calling this method to override the behaviour.
    /// <para><strong>Note:</strong></para>
    /// Replaces any previously registered <see cref="IMessageTransport" />.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configurator" /> is null.</exception>
    public static IMediatorBuilder UseKafkaTransport(this IMediatorBuilder configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton<IMessageTransport, KafkaMessageTransport>();

        return configurator;
    }
}
