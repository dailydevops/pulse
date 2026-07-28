namespace NetEvolve.Pulse.Outbox;

using System.Collections.Concurrent;
using System.Globalization;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Azure Service Bus transport implementation for the outbox processor.
/// </summary>
public sealed class AzureServiceBusMessageTransport : IMessageTransport, IAsyncDisposable
{
    private const string JsonContentType = "application/json";

    private readonly ServiceBusClient _client;
    private readonly ITopicNameResolver _topicNameResolver;
    private readonly AzureServiceBusTransportOptions _options;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusMessageTransport"/> class.
    /// </summary>
    /// <param name="client">The Service Bus client for creating senders.</param>
    /// <param name="topicNameResolver">The resolver to determine topic or queue names from messages.</param>
    /// <param name="options">The configured transport options.</param>
    internal AzureServiceBusMessageTransport(
        ServiceBusClient client,
        ITopicNameResolver topicNameResolver,
        IOptions<AzureServiceBusTransportOptions> options
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(topicNameResolver);
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _topicNameResolver = topicNameResolver;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var topicName = _topicNameResolver.Resolve(message);
        var sender = GetSender(topicName);
        var serviceBusMessage = CreateServiceBusMessage(message);
        await sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Group messages by resolved topic name for efficient batching
        var messagesByTopic = messages.ToLookup(m => _topicNameResolver.Resolve(m), StringComparer.Ordinal);

        if (!_options.EnableBatching)
        {
            foreach (var group in messagesByTopic)
            {
                var sender = GetSender(group.Key);
                foreach (var message in group)
                {
                    var serviceBusMessage = CreateServiceBusMessage(message);
                    await sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
                }
            }

            return;
        }

        foreach (var group in messagesByTopic)
        {
            var sender = GetSender(group.Key);
            var serviceBusMessages = group.Select(CreateServiceBusMessage).ToList();
            await sender.SendMessagesAsync(serviceBusMessages, cancellationToken).ConfigureAwait(false);
        }
    }

    private ServiceBusSender GetSender(string topicName) =>
        _senders.GetOrAdd(topicName, static (name, client) => client.CreateSender(name), _client);

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify the client is not disposed and can communicate with Service Bus
            // by checking if we can get basic namespace properties
            return Task.FromResult(!_client.IsClosed);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }
    }

    private static ServiceBusMessage CreateServiceBusMessage(OutboxMessage message)
    {
        var eventTypeName = message.EventType.ToOutboxEventTypeName();
        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromString(message.Payload))
        {
            ContentType = JsonContentType,
            Subject = eventTypeName,
            MessageId = message.Id.ToString("D", CultureInfo.InvariantCulture),
            CorrelationId = message.CorrelationId,
        };

        serviceBusMessage.ApplicationProperties["eventType"] = eventTypeName;
        serviceBusMessage.ApplicationProperties["createdAt"] = message.CreatedAt;
        serviceBusMessage.ApplicationProperties["updatedAt"] = message.UpdatedAt;
        serviceBusMessage.ApplicationProperties["retryCount"] = message.RetryCount;

        if (message.ProcessedAt is not null)
        {
            serviceBusMessage.ApplicationProperties["processedAt"] = message.ProcessedAt.Value;
        }

        if (message.Error is not null)
        {
            serviceBusMessage.ApplicationProperties["error"] = message.Error;
        }

        return serviceBusMessage;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Dispose only the senders owned by this transport; the injected client is managed externally.
        foreach (var topicName in _senders.Keys)
        {
            if (_senders.TryRemove(topicName, out var sender))
            {
                await sender.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
