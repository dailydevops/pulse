namespace NetEvolve.Pulse.Outbox;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Azure Queue Storage transport implementation for the outbox processor.
/// </summary>
/// <remarks>
/// The <see cref="QueueClient"/> is lazily initialized on first use. If
/// <see cref="AzureQueueStorageTransportOptions.CreateQueueIfNotExists"/> is <see langword="true"/>,
/// the queue is created automatically during initialization.
/// Messages are JSON-serialized and Base64-encoded before sending.
/// Raw message size must not exceed 48 KB (the Azure Queue Storage Base64-encoded limit of 64 KB).
/// </remarks>
public sealed class AzureQueueStorageMessageTransport : IMessageTransport, IDisposable
{
    internal const int MaxMessageSizeInBytes = 48 * 1024; // Raw 48 KB limit (64 KB after Base64 encoding)

    private readonly AzureQueueStorageTransportOptions _options;
    private readonly IPayloadSerializer _payloadSerializer;
    private readonly QueueClient? _queueClientOverride;
    private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
    private QueueClient? _queueClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureQueueStorageMessageTransport"/> class.
    /// </summary>
    /// <param name="options">The configured transport options.</param>
    /// <param name="payloadSerializer">The serializer used to serialize the outbox message envelope.</param>
    internal AzureQueueStorageMessageTransport(
        IOptions<AzureQueueStorageTransportOptions> options,
        IPayloadSerializer payloadSerializer
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        _options = options.Value;
        _payloadSerializer = payloadSerializer;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureQueueStorageMessageTransport"/> class
    /// with a pre-built queue client. Used for testing.
    /// </summary>
    /// <param name="options">The configured transport options.</param>
    /// <param name="payloadSerializer">The serializer used to serialize the outbox message envelope.</param>
    /// <param name="queueClient">A pre-built queue client to use instead of creating one from options.</param>
    internal AzureQueueStorageMessageTransport(
        IOptions<AzureQueueStorageTransportOptions> options,
        IPayloadSerializer payloadSerializer,
        QueueClient queueClient
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        ArgumentNullException.ThrowIfNull(queueClient);
        _options = options.Value;
        _payloadSerializer = payloadSerializer;
        _queueClientOverride = queueClient;
    }

    /// <inheritdoc />
    public void Dispose() => _initLock.Dispose();

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var json = SerializeMessage(_payloadSerializer, message);
        var rawBytes = Encoding.UTF8.GetBytes(json);

        if (rawBytes.Length > MaxMessageSizeInBytes)
        {
            throw new InvalidOperationException(
                $"Message size {rawBytes.Length} bytes exceeds the Azure Queue Storage limit of {MaxMessageSizeInBytes} bytes (48 KB raw / 64 KB Base64-encoded)."
            );
        }

        var base64 = Convert.ToBase64String(rawBytes);
        var queueClient = await GetQueueClientAsync(cancellationToken).ConfigureAwait(false);
        _ = await queueClient
            .SendMessageAsync(
                base64,
                visibilityTimeout: _options.MessageVisibilityTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages)
        {
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string SerializeMessage(IPayloadSerializer payloadSerializer, OutboxMessage message) =>
        payloadSerializer.Serialize(
            new
            {
                id = message.Id,
                eventType = message.EventType.ToOutboxEventTypeName(),
                payload = message.Payload,
                correlationId = message.CorrelationId,
                causationId = message.CausationId,
                createdAt = message.CreatedAt,
            }
        );

    [SuppressMessage(
        "Maintainability",
        "CA1508:Avoid dead conditional code",
        Justification = "Double-checked locking: the inner null check guards against concurrent initialization after the semaphore is acquired."
    )]
    private async Task<QueueClient> GetQueueClientAsync(CancellationToken cancellationToken)
    {
        if (_queueClientOverride is not null)
        {
            return _queueClientOverride;
        }

        if (_queueClient is not null)
        {
            return _queueClient;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock (double-checked locking pattern).
            if (_queueClient is not null)
            {
                return _queueClient;
            }

            var clientOptions = new QueueClientOptions(QueueClientOptions.ServiceVersion.V2025_11_05);

            QueueClient client;

            if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                client = new QueueClient(_options.ConnectionString, _options.QueueName, clientOptions);
            }
            else
            {
                var queueUri = new Uri(_options.QueueServiceUri!, _options.QueueName);
                client = new QueueClient(queueUri, new DefaultAzureCredential(), clientOptions);
            }

            if (_options.CreateQueueIfNotExists)
            {
                _ = await client.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            _queueClient = client;
            return _queueClient;
        }
        finally
        {
            _ = _initLock.Release();
        }
    }
}
