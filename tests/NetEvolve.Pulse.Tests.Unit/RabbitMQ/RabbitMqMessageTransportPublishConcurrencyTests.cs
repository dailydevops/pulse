namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::RabbitMQ.Client;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Concurrency invariant tests for publish serialization on
/// <see cref="RabbitMqMessageTransport"/>. RabbitMQ.Client's <see cref="IChannel"/> is
/// NOT thread-safe for concurrent publish calls, so the transport must never allow two
/// publishes to be in flight on the shared channel at the same time.
/// </summary>
[TestGroup("RabbitMQ")]
public sealed class RabbitMqMessageTransportPublishConcurrencyTests
{
    /// <summary>
    /// INVARIANT (thread-safety): two concurrent <c>SendAsync</c> calls on the singleton
    /// transport must be serialized onto the shared channel. The first publish is gated
    /// open; if the transport does not serialize publishes, the second call enters
    /// <c>BasicPublishAsync</c> while the first is still in flight and the fake channel
    /// observes two concurrent publishes.
    /// </summary>
    [Test]
    public async Task SendAsync_ConcurrentCalls_PublishesAreSerializedOnSharedChannel(
        CancellationToken cancellationToken
    )
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        // Pre-warm the channel so both sends reuse the same instance.
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
        var channel = connectionAdapter.CreatedChannels[0];

        // Gate the next publish so it stays in flight until we release it.
        var firstPublishEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.GateNextPublish(firstPublishEntered, releaseFirstPublish);

        var send1 = Task.Run(
            async () => await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false),
            cancellationToken
        );

        await firstPublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

        // Second send while the first publish is still in flight on the same channel.
        var send2 = Task.Run(
            async () => await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false),
            cancellationToken
        );

        // On an unserialized transport, the second publish enters BasicPublishAsync almost
        // immediately. Give it a generous window to do so before releasing the gate.
#pragma warning disable VSTHRD003 // Observation task is signaled by the fake channel, not started here
        _ = await Task.WhenAny(channel.SecondConcurrentPublishObserved, Task.Delay(500, cancellationToken))
            .ConfigureAwait(false);
#pragma warning restore VSTHRD003

        releaseFirstPublish.SetResult();
        await Task.WhenAll(send1, send2).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(channel.MaxConcurrentPublishes).IsEqualTo(1);
    }

    /// <summary>
    /// INVARIANT (thread-safety): a <c>SendAsync</c> racing a <c>SendBatchAsync</c> must
    /// not publish on the shared channel while a batch publish is in flight.
    /// </summary>
    [Test]
    public async Task SendAsync_RacingSendBatchAsync_PublishesAreSerializedOnSharedChannel(
        CancellationToken cancellationToken
    )
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(connectionAdapter, topicNameResolver);

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
        var channel = connectionAdapter.CreatedChannels[0];

        var batchPublishEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBatchPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.GateNextPublish(batchPublishEntered, releaseBatchPublish);

        var batchTask = Task.Run(
            async () =>
                await transport
                    .SendBatchAsync([CreateOutboxMessage(), CreateOutboxMessage()], cancellationToken)
                    .ConfigureAwait(false),
            cancellationToken
        );

        await batchPublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

        var sendTask = Task.Run(
            async () => await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false),
            cancellationToken
        );

#pragma warning disable VSTHRD003 // Observation task is signaled by the fake channel, not started here
        _ = await Task.WhenAny(channel.SecondConcurrentPublishObserved, Task.Delay(500, cancellationToken))
            .ConfigureAwait(false);
#pragma warning restore VSTHRD003

        releaseBatchPublish.SetResult();
        await Task.WhenAll(batchTask, sendTask)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(channel.MaxConcurrentPublishes).IsEqualTo(1);
    }

    private static RabbitMqMessageTransport CreateTransport(
        IRabbitMqConnectionAdapter connectionAdapter,
        ITopicNameResolver topicNameResolver,
        string exchangeName = "events"
    )
    {
        var options = Options.Create(new RabbitMqTransportOptions { ExchangeName = exchangeName });
        return new RabbitMqMessageTransport(connectionAdapter, topicNameResolver, options);
    }

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestRabbitMqEvent),
            Payload = """{"event":"sample"}""",
            CorrelationId = "corr-publish-race",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private sealed class FakeTopicNameResolver : ITopicNameResolver
    {
        public string Resolve(OutboxMessage message) => nameof(TestRabbitMqEvent);
    }

    private sealed record TestRabbitMqEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class FakeConnectionAdapter : IRabbitMqConnectionAdapter
    {
        public bool IsOpen { get; set; } = true;

        public List<FakeChannelAdapter> CreatedChannels { get; } = [];

        public Task<IRabbitMqChannelAdapter> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            var channel = new FakeChannelAdapter();
            CreatedChannels.Add(channel);
            return Task.FromResult<IRabbitMqChannelAdapter>(channel);
        }
    }

    private sealed class FakeChannelAdapter : IRabbitMqChannelAdapter
    {
        private readonly TaskCompletionSource _secondConcurrentPublishObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private int _activePublishes;
        private int _maxConcurrentPublishes;
        private (TaskCompletionSource entered, TaskCompletionSource release)? _publishGate;

        public bool IsOpen { get; set; } = true;

        public int MaxConcurrentPublishes => Volatile.Read(ref _maxConcurrentPublishes);

        public Task SecondConcurrentPublishObserved => _secondConcurrentPublishObserved.Task;

        public void GateNextPublish(TaskCompletionSource entered, TaskCompletionSource release) =>
            _publishGate = (entered, release);

        public async ValueTask BasicPublishAsync<TProperties>(
            string exchange,
            string routingKey,
            bool mandatory,
            TProperties basicProperties,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default
        )
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader
        {
            var current = Interlocked.Increment(ref _activePublishes);
            try
            {
                int max;
                do
                {
                    max = Volatile.Read(ref _maxConcurrentPublishes);
                } while (
                    current > max && Interlocked.CompareExchange(ref _maxConcurrentPublishes, current, max) != max
                );

                if (current > 1)
                {
                    _ = _secondConcurrentPublishObserved.TrySetResult();
                }

                if (_publishGate is { } gate)
                {
                    _publishGate = null;
                    _ = gate.entered.TrySetResult();
#pragma warning disable VSTHRD003 // TaskCompletionSource gate is signaled by the test, not started here
                    await gate.release.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
                }
            }
            finally
            {
                _ = Interlocked.Decrement(ref _activePublishes);
            }
        }

        public void Dispose() { }
    }
}
