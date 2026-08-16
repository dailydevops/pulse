namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
/// Concurrency invariant tests for <see cref="RabbitMqMessageTransport"/> publishing
/// through a channel pool. Unlike the previous single-shared-channel design, concurrent
/// <c>SendAsync</c> calls are now expected to run on separate, independently rented
/// channels rather than being serialized on one.
/// </summary>
[TestGroup("RabbitMQ")]
public sealed class RabbitMqMessageTransportPublishConcurrencyTests
{
    /// <summary>
    /// INVARIANT (pooling): two concurrent <c>SendAsync</c> calls rent distinct channels
    /// from the pool (up to the pool's capacity) instead of contending for a single shared
    /// channel, and each rented channel is returned back to the pool exactly once.
    /// </summary>
    [Test]
    public async Task SendAsync_ConcurrentCalls_RentSeparateChannelsFromPool(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channelPool = new GatingChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        using var transport = CreateTransport(channelPool, topicNameResolver);

        var firstPublishEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channelPool.GateNextRentedChannelPublish(firstPublishEntered, releaseFirstPublish);

        var send1 = Task.Run(
            async () => await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false),
            cancellationToken
        );

        await firstPublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

        // Second send while the first publish is still in flight on its own channel.
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        releaseFirstPublish.SetResult();
        await send1.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            // Two separate channels were rented, one per concurrent call.
            _ = await Assert.That(channelPool.RentCallCount).IsEqualTo(2);
            _ = await Assert.That(channelPool.ReturnCallCount).IsEqualTo(2);
        }
    }

    private static RabbitMqMessageTransport CreateTransport(
        IRabbitMqChannelPool channelPool,
        ITopicNameResolver topicNameResolver,
        string exchangeName = "events"
    )
    {
        var options = Options.Create(new RabbitMqTransportOptions { ExchangeName = exchangeName });
        return new RabbitMqMessageTransport(channelPool, topicNameResolver, options);
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

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "FakeChannelAdapter is a test double with no unmanaged resources; ownership is transferred to the caller via RentAsync."
    )]
    private sealed class GatingChannelPool : IRabbitMqChannelPool
    {
        private readonly object _gateLock = new();
        private int _rentCallCount;
        private int _returnCallCount;
        private (TaskCompletionSource entered, TaskCompletionSource release)? _publishGate;

        public int RentCallCount => Volatile.Read(ref _rentCallCount);

        public int ReturnCallCount => Volatile.Read(ref _returnCallCount);

        public void GateNextRentedChannelPublish(TaskCompletionSource entered, TaskCompletionSource release)
        {
            lock (_gateLock)
            {
                _publishGate = (entered, release);
            }
        }

        public ValueTask<IRabbitMqChannelAdapter> RentAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _ = Interlocked.Increment(ref _rentCallCount);

            (TaskCompletionSource entered, TaskCompletionSource release)? gate;
            lock (_gateLock)
            {
                gate = _publishGate;
                _publishGate = null;
            }

            return ValueTask.FromResult<IRabbitMqChannelAdapter>(new FakeChannelAdapter(gate));
        }

        public void Return(IRabbitMqChannelAdapter channel) => Interlocked.Increment(ref _returnCallCount);

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FakeChannelAdapter((TaskCompletionSource entered, TaskCompletionSource release)? publishGate)
        : IRabbitMqChannelAdapter
    {
        public bool IsOpen { get; set; } = true;

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
            cancellationToken.ThrowIfCancellationRequested();

            if (publishGate is { } gate)
            {
                _ = gate.entered.TrySetResult();
#pragma warning disable VSTHRD003 // TaskCompletionSource gate is signaled by the test, not started here
                await gate.release.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
        }

        public void Dispose() { }
    }
}
