namespace NetEvolve.Pulse.Tests.Integration.Kafka;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Kafka;
using TUnit.Core.Interfaces;

/// <summary>
/// Provides a shared Kafka container fixture for integration tests.
/// </summary>
public sealed class KafkaContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly KafkaContainer _container = new KafkaBuilder(
        /*dockerimage*/"confluentinc/cp-kafka:7.6.12"
    )
        .WithLogger(NullLogger.Instance)
        // Disabled so that "topic missing" scenarios (AutoCreateTopics = false on the transport)
        // exercise a real UnknownTopicOrPartition failure instead of the broker silently
        // auto-creating the topic on first produce.
        .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
        .Build();

    /// <summary>
    /// Gets the bootstrap servers address for the running Kafka container.
    /// </summary>
    public string ConnectionString => _container.GetBootstrapAddress();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <inheritdoc />
    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
