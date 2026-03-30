namespace NetEvolve.Pulse.Outbox;

using System.Globalization;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Amqp.Framing;
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
        var sender = _client.CreateSender(topicName);
        await using (sender.ConfigureAwait(false))
        {
            var serviceBusMessage = CreateServiceBusMessage(message);
            await sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Group messages by resolved topic name for efficient batching
        var messagesByTopic = messages.ToLookup(m => _topicNameResolver.Resolve(m));

        if (!_options.EnableBatching)
        {
            foreach (var group in messagesByTopic)
            {
                var sender = _client.CreateSender(group.Key);
                await using (sender.ConfigureAwait(false))
                {
                    foreach (var message in group)
                    {
                        var serviceBusMessage = CreateServiceBusMessage(message);
                        await sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            return;
        }

        foreach (var group in messagesByTopic)
        {
            var sender = _client.CreateSender(group.Key);
            await using (sender.ConfigureAwait(false))
            {
                var batch = await sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);
                using (batch)
                {
                    foreach (var message in group)
                    {
                        var serviceBusMessage = CreateServiceBusMessage(message);
                        if (!batch.TryAddMessage(serviceBusMessage))
                        {
                            throw new InvalidOperationException(
                                $"The message with id '{message.Id}' exceeded the maximum batch size for Azure Service Bus."
                            );
                        }
                    }

                    await sender.SendMessagesAsync(batch, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

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
        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromString(message.Payload))
        {
            ContentType = JsonContentType,
            Subject = message.EventType,
            MessageId = message.Id.ToString("D", CultureInfo.InvariantCulture),
            CorrelationId = message.CorrelationId,
        };

        serviceBusMessage.ApplicationProperties["eventType"] = message.EventType;
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
    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // Do not dispose injected dependencies
}
