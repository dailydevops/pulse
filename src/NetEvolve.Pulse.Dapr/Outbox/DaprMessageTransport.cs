namespace NetEvolve.Pulse.Outbox;

using System.Globalization;
using System.Text;
using Dapr.Client;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Message transport that publishes outbox messages to Dapr pub/sub topics.
/// </summary>
/// <remarks>
/// <para><strong>Topic Resolution:</strong></para>
/// Each message is published to a topic resolved by <see cref="ITopicNameResolver"/>.
/// By default, the simple class name of the event type is used (e.g., <c>"OrderCreated"</c>).
/// <para><strong>Payload:</strong></para>
/// The raw JSON payload from <see cref="OutboxMessage.Payload"/> is published as the CloudEvent data
/// with content type <c>application/json</c>.
/// <para><strong>CloudEvent attributes:</strong></para>
/// The CloudEvent <c>id</c> is set to <see cref="OutboxMessage.Id"/> (format <c>"D"</c>) and the <c>type</c>
/// to the outbox event type name of <see cref="OutboxMessage.EventType"/> via the <c>cloudevent.id</c> and
/// <c>cloudevent.type</c> publish metadata, so redeliveries of the same message keep the same <c>id</c>.
/// Trace attributes are left to the Dapr sidecar.
/// <para><strong>Prerequisites:</strong></para>
/// Requires <see cref="DaprClient"/> to be registered in the DI container, e.g. via <c>services.AddDaprClient()</c>
/// from the <c>Dapr.AspNetCore</c> package.
/// </remarks>
internal sealed class DaprMessageTransport : IMessageTransport
{
    /// <summary>The Dapr client used to publish events to the configured pub/sub component.</summary>
    private readonly DaprClient _daprClient;

    /// <summary>The resolved transport options controlling the pub/sub component name.</summary>
    private readonly DaprMessageTransportOptions _options;

    /// <summary>The topic name resolver used to determine the Dapr topic name from an outbox message.</summary>
    private readonly ITopicNameResolver _topicNameResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="DaprMessageTransport"/> class.
    /// </summary>
    /// <param name="daprClient">The Dapr client for publishing events.</param>
    /// <param name="topicNameResolver">The topic name resolver for determining topic names from outbox messages.</param>
    /// <param name="options">The transport options.</param>
    /// <param name="payloadSerializer">
    /// The payload serializer, kept for API compatibility with the other outbox transports; not used
    /// for publishing.
    /// </param>
    public DaprMessageTransport(
        DaprClient daprClient,
        ITopicNameResolver topicNameResolver,
        IOptions<DaprMessageTransportOptions> options,
        IPayloadSerializer payloadSerializer
    )
    {
        ArgumentNullException.ThrowIfNull(daprClient);
        ArgumentNullException.ThrowIfNull(topicNameResolver);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(payloadSerializer);

        _daprClient = daprClient;
        _topicNameResolver = topicNameResolver;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(message);

        var topicName = _topicNameResolver.Resolve(message);
        var payload = Encoding.UTF8.GetBytes(message.Payload);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cloudevent.id"] = message.Id.ToString("D", CultureInfo.InvariantCulture),
            ["cloudevent.type"] = message.EventType.ToOutboxEventTypeName(),
        };

        await _daprClient
            .PublishByteEventAsync(
                _options.PubSubName,
                topicName,
                payload,
                "application/json",
                metadata,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        _daprClient.CheckHealthAsync(cancellationToken);
}
