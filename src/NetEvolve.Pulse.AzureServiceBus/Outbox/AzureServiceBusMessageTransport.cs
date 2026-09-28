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

    // Entities that rejected a multi-message batch (partitioned with duplicate detection). Messages for
    // these entities are sent one at a time for the lifetime of this transport.
    private readonly ConcurrentDictionary<string, bool> _individualSendEntities = new(StringComparer.Ordinal);

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
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(message);

        var topicName = _topicNameResolver.Resolve(message);
        var sender = GetSender(topicName);
        var serviceBusMessage = CreateServiceBusMessage(message);
        await sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(messages);

        // Group messages by resolved topic name for efficient batching
        var messagesByTopic = messages.ToLookup(m => _topicNameResolver.Resolve(m), StringComparer.Ordinal);

        foreach (var group in messagesByTopic)
        {
            var sender = GetSender(group.Key);
            if (!_options.EnableBatching || _individualSendEntities.ContainsKey(group.Key))
            {
                await SendIndividuallyAsync(sender, group.Select(CreateServiceBusMessage), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await SendChunkedBatchesAsync(group.Key, sender, group, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendIndividuallyAsync(
        ServiceBusSender sender,
        IEnumerable<ServiceBusMessage> messages,
        CancellationToken cancellationToken
    )
    {
        foreach (var message in messages)
        {
            await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private ServiceBusSender GetSender(string topicName) =>
        _senders.GetOrAdd(topicName, static (name, client) => client.CreateSender(name), _client);

    private async Task SendChunkedBatchesAsync(
        string entityName,
        ServiceBusSender sender,
        IEnumerable<OutboxMessage> messages,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // ServiceBusMessageBatch does not expose its messages, so track them for the individual-send fallback.
        var pending = new List<ServiceBusMessage>();
        var batch = await sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sendIndividually = false;
            foreach (var message in messages)
            {
                var serviceBusMessage = CreateServiceBusMessage(message);
                if (sendIndividually)
                {
                    await sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (batch.TryAddMessage(serviceBusMessage))
                {
                    pending.Add(serviceBusMessage);
                    continue;
                }

                if (batch.Count > 0)
                {
                    if (
                        !await TrySendBatchAsync(entityName, sender, batch, pending, cancellationToken)
                            .ConfigureAwait(false)
                    )
                    {
                        sendIndividually = true;
                        await sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    batch.Dispose();
                    pending.Clear();
                    batch = await sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!batch.TryAddMessage(serviceBusMessage))
                {
                    throw new ServiceBusException(
                        $"The message with id '{serviceBusMessage.MessageId}' exceeds the maximum allowed batch size.",
                        ServiceBusFailureReason.MessageSizeExceeded
                    );
                }

                pending.Add(serviceBusMessage);
            }

            if (!sendIndividually && batch.Count > 0)
            {
                _ = await TrySendBatchAsync(entityName, sender, batch, pending, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            batch.Dispose();
        }
    }

    /// <summary>
    /// Sends <paramref name="batch"/>. When the service rejects a multi-message batch because the messages
    /// carry distinct partition keys (partitioned entity with duplicate detection, where the unique
    /// <see cref="ServiceBusMessage.MessageId"/> is the partition key), the batch was not enqueued at all,
    /// so its messages are sent one at a time and the entity is remembered for individual sending.
    /// </summary>
    /// <returns><see langword="true"/> when the batch was sent; <see langword="false"/> when it fell back to individual sends.</returns>
    private async Task<bool> TrySendBatchAsync(
        string entityName,
        ServiceBusSender sender,
        ServiceBusMessageBatch batch,
        List<ServiceBusMessage> pending,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await sender.SendMessagesAsync(batch, cancellationToken).ConfigureAwait(false);
            return true;
        }
        // The SDK maps the service condition amqp:not-allowed to InvalidOperationException.
        catch (InvalidOperationException ex) when (ex is not ObjectDisposedException && batch.Count > 1)
        {
            _individualSendEntities[entityName] = true;
            await SendIndividuallyAsync(sender, pending, cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
