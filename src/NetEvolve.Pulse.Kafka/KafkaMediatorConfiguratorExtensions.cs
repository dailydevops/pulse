namespace NetEvolve.Pulse;

using System.Linq;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods on <see cref="IMediatorConfigurator" /> for registering the Apache Kafka
/// outbox transport.
/// </summary>
public static class KafkaMediatorConfiguratorExtensions
{
    /// <summary>
    /// Registers the Kafka outbox transport so that outbox messages are produced to a Kafka topic
    /// using the Confluent.Kafka producer with <c>Acks.All</c>.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">
    /// An optional delegate to configure <see cref="KafkaTransportOptions" />.
    /// </param>
    /// <returns>The same <paramref name="configurator" /> instance for chaining.</returns>
    public static IMediatorConfigurator UseKafkaTransport(
        this IMediatorConfigurator configurator,
        Action<KafkaTransportOptions>? configureOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);

        var services = configurator.Services;

        _ = services.AddOptions<KafkaTransportOptions>();
        if (configureOptions is not null)
        {
            _ = services.Configure(configureOptions);
        }

        services.TryAddSingleton(static sp =>
        {
            var options = sp.GetRequiredService<IOptions<KafkaTransportOptions>>().Value;
            ValidateOptions(options);

            var config = options.ProducerConfig ?? new ProducerConfig();
            config.BootstrapServers = options.BootstrapServers;
            config.Acks = Acks.All;

            return new ProducerBuilder<string, string>(config).Build();
        });

        services.TryAddSingleton<IAdminClient>(static sp =>
        {
            var options = sp.GetRequiredService<IOptions<KafkaTransportOptions>>().Value;
            ValidateOptions(options);

            var config = new AdminClientConfig { BootstrapServers = options.BootstrapServers };

            return new AdminClientBuilder(config).Build();
        });

        services.TryAddSingleton<IKafkaProducerAdapter, KafkaProducerAdapter>();
        services.TryAddSingleton<IKafkaAdminAdapter, KafkaAdminAdapter>();

        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IMessageTransport));
        if (existing is not null)
        {
            _ = services.Remove(existing);
        }

        _ = services.AddSingleton<IMessageTransport, KafkaMessageTransport>();

        return configurator;
    }

    private static void ValidateOptions(KafkaTransportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
        {
            throw new InvalidOperationException(
                "Kafka bootstrap servers must be configured via KafkaTransportOptions.BootstrapServers."
            );
        }
    }
}
