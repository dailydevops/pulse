namespace NetEvolve.Pulse;

using System.Text.Json;
using Dapr.Client;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Message transport that publishes outbox messages to Dapr pub/sub topics.
/// </summary>
/// <remarks>
/// <para><strong>Topic Resolution:</strong></para>
/// Each message is published to a topic resolved by <see cref="DaprMessageTransportOptions.TopicNameResolver"/>.
/// By default, the simple class name of the event type is used (e.g., <c>"OrderCreated"</c>).
/// <para><strong>Payload:</strong></para>
/// The raw JSON payload from <see cref="OutboxMessage.Payload"/> is published as the CloudEvent data.
/// <para><strong>Prerequisites:</strong></para>
/// Requires <see cref="DaprClient"/> to be registered in the DI container, e.g. via <c>services.AddDaprClient()</c>
/// from the <c>Dapr.AspNetCore</c> package.
/// </remarks>
internal sealed class DaprMessageTransport : IMessageTransport
{
    /// <summary>The Dapr client used to publish events to the configured pub/sub component.</summary>
    private readonly DaprClient _daprClient;

    /// <summary>The resolved transport options controlling the pub/sub component name and topic resolution.</summary>
    private readonly DaprMessageTransportOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DaprMessageTransport"/> class.
    /// </summary>
    /// <param name="daprClient">The Dapr client for publishing events.</param>
    /// <param name="options">The transport options.</param>
    public DaprMessageTransport(DaprClient daprClient, IOptions<DaprMessageTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(daprClient);
        ArgumentNullException.ThrowIfNull(options);

        _daprClient = daprClient;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var topicName = _options.TopicNameResolver(message);
        var payload = JsonSerializer.Deserialize<JsonElement>(message.Payload);

        await _daprClient
            .PublishEventAsync(_options.PubSubName, topicName, payload, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        _daprClient.CheckHealthAsync(cancellationToken);
}
