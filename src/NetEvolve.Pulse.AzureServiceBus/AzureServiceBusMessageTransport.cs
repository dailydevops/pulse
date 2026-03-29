namespace NetEvolve.Pulse;

using System.Text;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Message transport that publishes outbox messages to an Azure Service Bus queue or topic.
/// </summary>
/// <remarks>
/// <para><strong>Authentication:</strong></para>
/// When <see cref="AzureServiceBusTransportOptions.ConnectionString"/> is set, it is used directly.
/// Otherwise, <c>DefaultAzureCredential</c> is used together with
/// <see cref="AzureServiceBusTransportOptions.FullyQualifiedNamespace"/> for Managed Identity support.
/// <para><strong>Payload:</strong></para>
/// Each <see cref="OutboxMessage.Payload"/> is forwarded as the body of a <c>ServiceBusMessage</c>
/// with the <c>Content-Type</c> set to <c>application/json</c>.
/// <para><strong>Batch Sending:</strong></para>
/// When <see cref="AzureServiceBusTransportOptions.EnableBatching"/> is <c>true</c>,
/// <see cref="SendBatchAsync"/> uses <c>ServiceBusMessageBatch</c> for efficient bulk delivery.
/// When disabled, messages are sent individually via <see cref="SendAsync"/>.
/// </remarks>
internal sealed class AzureServiceBusMessageTransport : IMessageTransport, IAsyncDisposable
{
    /// <summary>The Service Bus client used to create senders.</summary>
    private readonly ServiceBusClient _client;

    /// <summary>The Service Bus sender used to send messages to the configured entity.</summary>
    private readonly ServiceBusSender _sender;

    /// <summary>The Service Bus administration client used for health checks.</summary>
    private readonly ServiceBusAdministrationClient _administrationClient;

    /// <summary>The resolved transport options.</summary>
    private readonly AzureServiceBusTransportOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusMessageTransport"/> class.
    /// </summary>
    /// <param name="options">The transport options.</param>
    public AzureServiceBusMessageTransport(IOptions<AzureServiceBusTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            _client = new ServiceBusClient(_options.ConnectionString);
            _administrationClient = new ServiceBusAdministrationClient(_options.ConnectionString);
        }
        else
        {
            var credential = new DefaultAzureCredential();
            _client = new ServiceBusClient(_options.FullyQualifiedNamespace, credential);
            _administrationClient = new ServiceBusAdministrationClient(_options.FullyQualifiedNamespace, credential);
        }

        _sender = _client.CreateSender(_options.EntityPath);
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var serviceBusMessage = CreateServiceBusMessage(message);
        await _sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
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

        var currentBatch = await _sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var message in messages)
            {
                var serviceBusMessage = CreateServiceBusMessage(message);
                if (!currentBatch.TryAddMessage(serviceBusMessage))
                {
                    // Current message does not fit — send the accumulated batch first
                    await _sender.SendMessagesAsync(currentBatch, cancellationToken).ConfigureAwait(false);
                    currentBatch.Dispose();
                    currentBatch = await _sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);

                    // Add the oversized message to the fresh batch
                    if (!currentBatch.TryAddMessage(serviceBusMessage))
                    {
                        // Message is too large even for an empty batch — send it individually
                        await _sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            if (currentBatch.Count > 0)
            {
                await _sender.SendMessagesAsync(currentBatch, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            currentBatch.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The result is intentionally discarded; a successful response confirms connectivity.
            _ = await _administrationClient
                .GetQueueRuntimePropertiesAsync(_options.EntityPath, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (ServiceBusException)
        {
            // Queue not found — try topic
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            // The result is intentionally discarded; a successful response confirms connectivity.
            _ = await _administrationClient
                .GetTopicRuntimePropertiesAsync(_options.EntityPath, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a <see cref="ServiceBusMessage"/> from an <see cref="OutboxMessage"/>.
    /// </summary>
    /// <param name="message">The outbox message to convert.</param>
    /// <returns>A new <see cref="ServiceBusMessage"/> with the outbox payload as the body.</returns>
    private static ServiceBusMessage CreateServiceBusMessage(OutboxMessage message)
    {
        var serviceBusMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(message.Payload))
        {
            ContentType = "application/json",
            MessageId = message.Id.ToString(),
        };

        serviceBusMessage.ApplicationProperties["EventType"] = message.EventType;
        if (message.CorrelationId is not null)
        {
            serviceBusMessage.ApplicationProperties["CorrelationId"] = message.CorrelationId;
        }

        return serviceBusMessage;
    }
}
