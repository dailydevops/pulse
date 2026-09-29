namespace NetEvolve.Pulse.Outbox;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Apache Kafka transport that delivers outbox messages to Kafka topics using the Confluent.Kafka
/// producer with <c>Acks.All</c> for durability.
/// </summary>
/// <remarks>
/// The Confluent.Kafka <see cref="IProducer{TKey,TValue}" /> and <see cref="IAdminClient" /> must be
/// registered in the DI container by the caller before using this transport.
/// Topic routing is determined by the registered <see cref="ITopicNameResolver" />.
/// </remarks>
public sealed partial class KafkaMessageTransport : IMessageTransport, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly IAdminClient _adminClient;
    private readonly ITopicNameResolver _topicNameResolver;
    private readonly KafkaTransportOptions _options;
    private readonly ILogger<KafkaMessageTransport> _logger;
    private readonly ConcurrentDictionary<string, bool> _ensuredTopics = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of <see cref="KafkaMessageTransport" />.
    /// </summary>
    /// <param name="producer">The Kafka producer, registered in DI by the caller.</param>
    /// <param name="adminClient">The Kafka admin client, registered in DI by the caller.</param>
    /// <param name="topicNameResolver">The resolver that maps each outbox message to a Kafka topic name.</param>
    /// <param name="options">The transport options.</param>
    /// <param name="logger">
    /// Optional logger for topic auto-creation warnings. When <see langword="null" />, nothing is logged.
    /// </param>
    public KafkaMessageTransport(
        IProducer<string, string> producer,
        IAdminClient adminClient,
        ITopicNameResolver topicNameResolver,
        IOptions<KafkaTransportOptions> options,
        ILogger<KafkaMessageTransport>? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(adminClient);
        ArgumentNullException.ThrowIfNull(topicNameResolver);
        ArgumentNullException.ThrowIfNull(options);

        _producer = producer;
        _adminClient = adminClient;
        _topicNameResolver = topicNameResolver;
        _options = options.Value;
        _logger = logger ?? NullLogger<KafkaMessageTransport>.Instance;
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(message);

        var topic = _topicNameResolver.Resolve(message);

        await EnsureTopicAsync(topic, cancellationToken).ConfigureAwait(false);

        var kafkaMessage = CreateKafkaMessage(message);

        _ = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "Intentional fire-and-forget batch pattern. Produce() enqueues messages with a delivery-report callback; awaiting ProduceAsync() per message would serialize delivery and defeat the purpose of batching. The method is async only to await EnsureTopicAsync() for topic auto-creation before the fire-and-forget send loop."
    )]
    [SuppressMessage(
        "Usage",
        "NE0009:Method or local function has a CancellationToken parameter but does not check for cancellation at the start of its body",
        Justification = "Cancellation is intentionally not observed until the final Flush(cancellationToken) call. Produce() is a synchronous, non-blocking librdkafka enqueue that should still run for a cancelled batch so Flush can drain and observe the cancellation, letting worker shutdown proceed promptly instead of leaving enqueued messages stranded."
    )]
    public async Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var errors = new ConcurrentBag<Exception>();
        var failedTopics = new Dictionary<string, Exception>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            var topic = _topicNameResolver.Resolve(message);

            if (failedTopics.TryGetValue(topic, out var topicError))
            {
                errors.Add(topicError);
                continue;
            }

            try
            {
                await EnsureTopicAsync(topic, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Keep going so messages already enqueued are still flushed and their outcome reported.
                // A cancellation is observed again by Flush(cancellationToken) below.
                failedTopics[topic] = ex;
                errors.Add(ex);
                continue;
            }

            var kafkaMessage = CreateKafkaMessage(message);

            try
            {
                _producer.Produce(
                    topic,
                    kafkaMessage,
                    report =>
                    {
                        if (report.Error.IsError)
                        {
                            errors.Add(new ProduceException<string, string>(report.Error, report));
                        }
                    }
                );
            }
            catch (ProduceException<string, string> ex)
            {
                errors.Add(ex);
            }
        }

        // Use the cancellation-token-aware overload so worker shutdown does not block
        // indefinitely when the broker is unreachable. Confluent.Kafka's IProducer.Flush(CancellationToken)
        // throws OperationCanceledException when the token fires, which is the contract callers expect.
        // Offloaded via Task.Run because Flush is a synchronous, potentially long-running call and
        // Confluent.Kafka has no FlushAsync; running it on the caller's thread would otherwise pin a
        // thread-pool thread for the duration of the flush.
        await Task.Run(() => _producer.Flush(cancellationToken), CancellationToken.None).ConfigureAwait(false);

        if (!errors.IsEmpty)
        {
            throw new AggregateException(errors);
        }
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
                () =>
                {
                    try
                    {
                        var metadata = _adminClient.GetMetadata(TimeSpan.FromSeconds(5));
                        return metadata.Brokers.Count > 0;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                },
                cancellationToken
            )
            .WaitAsync(cancellationToken);

    [SuppressMessage(
        "Usage",
        "NE0009:Method or local function has a CancellationToken parameter but does not check for cancellation at the start of its body",
        Justification = "The no-op short-circuit (topic auto-creation disabled, or topic already ensured) must run even for an already-cancelled token, so callers such as SendBatchAsync can defer cancellation observation to their own Flush call instead of failing here."
    )]
    private async Task EnsureTopicAsync(string topic, CancellationToken cancellationToken)
    {
        if (!_options.AutoCreateTopics || _ensuredTopics.ContainsKey(topic))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var configs = new Dictionary<string, string>();

        if (_options.MessageRetention.HasValue)
        {
            configs["retention.ms"] = ((long)_options.MessageRetention.Value.TotalMilliseconds).ToString(
                CultureInfo.InvariantCulture
            );
        }

        var spec = new TopicSpecification
        {
            Name = topic,
            NumPartitions = _options.DefaultPartitionCount,
            ReplicationFactor = _options.DefaultReplicationFactor,
            Configs = configs.Count > 0 ? configs : null,
        };

        try
        {
            await _adminClient.CreateTopicsAsync([spec]).WaitAsync(cancellationToken).ConfigureAwait(false);
            _ = _ensuredTopics.TryAdd(topic, true);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(static r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            _ = _ensuredTopics.TryAdd(topic, true);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(static r => IsAuthorizationFailure(r.Error.Code)))
        {
            // The broker checks CREATE before it checks existence, so an existing topic still reports an
            // authorization failure. Stop trying to create it and let the produce call surface the real error.
            _ = _ensuredTopics.TryAdd(topic, true);

            if (!TopicExists(topic))
            {
                LogTopicCreationNotAuthorized(_logger, topic, ex.Results[0].Error.Code);
            }
        }
        catch (CreateTopicsException ex)
        {
            throw new InvalidOperationException(
                $"Kafka topic '{topic}' could not be created: {ex.Message} Pre-provision the topic and set "
                    + $"{nameof(KafkaTransportOptions)}.{nameof(KafkaTransportOptions.AutoCreateTopics)} to false, "
                    + "or fix the topic defaults or broker policy.",
                ex
            );
        }
    }

    private static bool IsAuthorizationFailure(ErrorCode code) =>
        code is ErrorCode.TopicAuthorizationFailed or ErrorCode.ClusterAuthorizationFailed;

    private bool TopicExists(string topic)
    {
        try
        {
            return _adminClient
                .GetMetadata(topic, TimeSpan.FromSeconds(5))
                .Topics.Exists(t => string.Equals(t.Topic, topic, StringComparison.Ordinal) && !t.Error.IsError);
        }
        catch (KafkaException)
        {
            return false;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Not authorized to create Kafka topic '{Topic}' ({ErrorCode}) and its existence could not be confirmed. Producing anyway; pre-provision the topic and set KafkaTransportOptions.AutoCreateTopics to false to skip creation."
    )]
    private static partial void LogTopicCreationNotAuthorized(ILogger logger, string topic, ErrorCode errorCode);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // Do not dispose injected dependencies

    private static Message<string, string> CreateKafkaMessage(OutboxMessage message)
    {
        var eventTypeName = message.EventType.ToOutboxEventTypeName();
        var headers = new Headers
        {
            { "eventType", Encoding.UTF8.GetBytes(eventTypeName) },
            { "contentType", "application/json"u8.ToArray() },
        };

        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            headers.Add("correlationId", Encoding.UTF8.GetBytes(message.CorrelationId));
        }

        return new Message<string, string>
        {
            Key = message.Id.ToString("D", CultureInfo.InvariantCulture),
            Value = message.Payload,
            Headers = headers,
        };
    }
}
