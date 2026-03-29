namespace NetEvolve.Pulse.Outbox;

using System.Globalization;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;

/// <summary>
/// Azure Service Bus transport implementation for the outbox processor.
/// </summary>
public sealed class AzureServiceBusMessageTransport : IMessageTransport, IAsyncDisposable
{
    private const string JsonContentType = "application/json";

    private readonly IServiceBusSenderAdapter _senderAdapter;
    private readonly IServiceBusAdministrationClientAdapter _administrationClientAdapter;
    private readonly AzureServiceBusTransportOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusMessageTransport"/> class.
    /// </summary>
    /// <param name="senderAdapter">The sender adapter responsible for creating batches and sending messages.</param>
    /// <param name="administrationClientAdapter">The administration client adapter used for health checks.</param>
    /// <param name="options">The configured transport options.</param>
    internal AzureServiceBusMessageTransport(
        IServiceBusSenderAdapter senderAdapter,
        IServiceBusAdministrationClientAdapter administrationClientAdapter,
        IOptions<AzureServiceBusTransportOptions> options
    )
    {
        ArgumentNullException.ThrowIfNull(senderAdapter);
        ArgumentNullException.ThrowIfNull(administrationClientAdapter);
        ArgumentNullException.ThrowIfNull(options);

        _senderAdapter = senderAdapter;
        _administrationClientAdapter = administrationClientAdapter;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var serviceBusMessage = CreateServiceBusMessage(message);
        await _senderAdapter.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (!_options.EnableBatching)
        {
            foreach (var message in messages)
            {
                await SendAsync(message, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var batch = await _senderAdapter.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);
        foreach (var message in messages)
        {
            var serviceBusMessage = CreateServiceBusMessage(message);
            if (!batch.TryAddMessage(serviceBusMessage))
            {
                throw new InvalidOperationException(
                    $"The message with id '{message.Id}' exceeded the maximum batch size for Azure Service Bus."
                );
            }
        }

        await _senderAdapter.SendMessagesAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return _options.EntityType switch
            {
                AzureServiceBusEntityType.Queue => await _administrationClientAdapter
                    .TryGetQueueRuntimePropertiesAsync(_options.EntityPath, cancellationToken)
                    .ConfigureAwait(false),
                AzureServiceBusEntityType.Topic => await _administrationClientAdapter
                    .TryGetTopicRuntimePropertiesAsync(_options.EntityPath, cancellationToken)
                    .ConfigureAwait(false),
                _ => false,
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static Azure.Messaging.ServiceBus.ServiceBusMessage CreateServiceBusMessage(OutboxMessage message)
    {
        var serviceBusMessage = new Azure.Messaging.ServiceBus.ServiceBusMessage(BinaryData.FromString(message.Payload))
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
    public async ValueTask DisposeAsync() => await _senderAdapter.DisposeAsync().ConfigureAwait(false);
}
