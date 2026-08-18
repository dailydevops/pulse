namespace NetEvolve.Pulse.Outbox;

using System.Globalization;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using StackExchange.Redis;

/// <summary>
/// Message transport that publishes outbox messages to a Redis stream.
/// </summary>
/// <remarks>
/// <para><strong>Stream Layout:</strong></para>
/// Each message is appended to the configured stream via <c>XADD</c> with its fields flattened
/// into name/value entries (<c>id</c>, <c>eventType</c>, <c>payload</c>, <c>correlationId</c>,
/// <c>causationId</c>, <c>retryCount</c>, <c>createdAt</c>).
/// <para><strong>Consumer Group Bootstrapping:</strong></para>
/// When <see cref="RedisStreamsTransportOptions.CreateStreamIfNotExists"/> is enabled, the
/// configured consumer group is created once per transport instance, lazily, on the first
/// send. If the group already exists (e.g. created by another process or a prior run), the
/// resulting <c>BUSYGROUP</c> error is treated as success.
/// <para><strong>Prerequisites:</strong></para>
/// <see cref="IConnectionMultiplexer"/> must be registered in the DI container by the caller
/// before using this transport.
/// </remarks>
internal sealed class RedisStreamsMessageTransport : IMessageTransport, IDisposable
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisStreamsTransportOptions _options;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _consumerGroupEnsured;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisStreamsMessageTransport"/> class.
    /// </summary>
    /// <param name="multiplexer">The Redis connection multiplexer.</param>
    /// <param name="options">The transport options.</param>
    internal RedisStreamsMessageTransport(
        IConnectionMultiplexer multiplexer,
        IOptions<RedisStreamsTransportOptions> options
    )
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(options);

        _multiplexer = multiplexer;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(message);

        var database = _multiplexer.GetDatabase(_options.Database);

        await EnsureConsumerGroupAsync(database, cancellationToken).ConfigureAwait(false);

        var fields = new NameValueEntry[]
        {
            new("id", message.Id.ToString()),
            new("eventType", message.EventType.ToOutboxEventTypeName()),
            new("payload", message.Payload),
            new("correlationId", message.CorrelationId ?? string.Empty),
            new("causationId", message.CausationId ?? string.Empty),
            new("retryCount", message.RetryCount.ToString(CultureInfo.InvariantCulture)),
            new("createdAt", message.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
        };

        _ = await database.StreamAddAsync(_options.StreamKey, fields).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Overridden to iterate <see cref="SendAsync"/> per message sequentially in enumeration
    /// order; no atomic or pipelined batching is performed.
    /// </remarks>
    public async Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_multiplexer.IsConnected);

    /// <summary>
    /// Ensures the configured consumer group exists on the stream, creating it lazily and at
    /// most once per transport instance when <see cref="RedisStreamsTransportOptions.CreateStreamIfNotExists"/>
    /// is enabled.
    /// </summary>
    /// <param name="database">The Redis database to create the consumer group on.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    private async Task EnsureConsumerGroupAsync(IDatabase database, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.CreateStreamIfNotExists || Volatile.Read(ref _consumerGroupEnsured))
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_consumerGroupEnsured)
            {
                return;
            }

            try
            {
                _ = await database
                    .StreamCreateConsumerGroupAsync(
                        _options.StreamKey,
                        _options.ConsumerGroupName,
                        "$",
                        createStream: true
                    )
                    .ConfigureAwait(false);
            }
            catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
            {
                // The consumer group already exists (e.g. created by another process or a
                // prior run); treat this as success.
            }

            _consumerGroupEnsured = true;
        }
        finally
        {
            _ = _initializationLock.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Disposal is single-shot under concurrency: the first thread to flip <c>_disposed</c> via
    /// <see cref="Interlocked.Exchange(ref int, int)"/> disposes <see cref="_initializationLock"/>;
    /// all subsequent callers (including concurrent ones) are no-ops.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _initializationLock.Dispose();
    }
}
