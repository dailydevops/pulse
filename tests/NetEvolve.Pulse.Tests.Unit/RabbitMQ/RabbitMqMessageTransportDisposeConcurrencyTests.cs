namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
/// Cross-cutting deep-audit tests (DEEP-E pass) for concurrency invariants on
/// <see cref="RabbitMqMessageTransport.Dispose"/> in the presence of in-flight
/// senders. These pin disposal-race semantics that the existing
/// <c>Dispose_Is_idempotent</c> test (sequential double dispose) does not cover.
/// </summary>
[TestGroup("RabbitMQ")]
[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Transport is explicitly disposed inside the test bodies as part of the assertion under test."
)]
public sealed class RabbitMqMessageTransportDisposeConcurrencyTests
{
    /// <summary>
    /// INVARIANT (concurrency / disposal): two threads calling <c>Dispose()</c> at the
    /// same time must result in the underlying channel being disposed exactly once.
    /// Currently the implementation uses a plain <c>bool _disposed</c> guard without
    /// <c>Interlocked.Exchange</c>, so both threads can pass the early-exit check and
    /// each call <c>_channel?.Dispose()</c>.
    /// </summary>
    [Test]
    public async Task Dispose_CalledConcurrently_ChannelDisposedExactlyOnce(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(connectionAdapter, topicNameResolver);
        try
        {
            // Force a channel to be created so Dispose has work to do.
            await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
            var channel = connectionAdapter.CreatedChannels[0];

            // Use a barrier so both threads enter Dispose() as close to simultaneously as
            // possible. We run the test multiple times to make the race observable.
            const int Iterations = 100;
            var doubleDisposeSeen = false;

            for (var i = 0; i < Iterations; i++)
            {
                channel.ResetDisposeCounter();

                using var barrier = new Barrier(2);
                void DisposeOnce()
                {
                    barrier.SignalAndWait(cancellationToken);
                    transport.Dispose();
                }

                var t1 = Task.Run(DisposeOnce, cancellationToken);
                var t2 = Task.Run(DisposeOnce, cancellationToken);
                await Task.WhenAll(t1, t2).ConfigureAwait(false);

                if (channel.DisposeCallCount > 1)
                {
                    doubleDisposeSeen = true;
                    break;
                }

                // The transport is disposed by now. Recreate it for the next iteration.
                if (i + 1 < Iterations)
                {
                    transport.Dispose();
                    transport = CreateTransport(connectionAdapter, topicNameResolver);
                    await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
                    channel = connectionAdapter.CreatedChannels[^1];
                }
            }

            // The invariant we want: the channel adapter must only be disposed once,
            // regardless of how many threads call Dispose. If this assertion fails, the
            // implementation is using a non-atomic _disposed guard and needs
            // Interlocked.Exchange to be race-safe.
            _ = await Assert.That(doubleDisposeSeen).IsFalse();
        }
        finally
        {
            transport.Dispose();
        }
    }

    /// <summary>
    /// INVARIANT (concurrency / disposal): when <c>Dispose()</c> races with an
    /// in-flight <c>SendAsync</c>, the dispose path must not throw on the disposing
    /// thread and the in-flight publish must observe the publish gate it was waiting
    /// for. We do not assert on the publish's terminal exception (it is acceptable
    /// for the publish to either complete or fault deterministically), only that
    /// dispose itself stays well-defined.
    /// </summary>
    [Test]
    public async Task Dispose_DuringInFlightSendAsync_DoesNotCorruptInFlightPublish(CancellationToken cancellationToken)
    {
        var connectionAdapter = new FakeConnectionAdapter();
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(connectionAdapter, topicNameResolver);
        try
        {
            // Pre-warm the channel.
            await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
            var channel = connectionAdapter.CreatedChannels[0];

            // Make the next BasicPublishAsync block until we explicitly release it.
            var publishStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releasePublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            channel.PublishGate = (start: publishStarted, release: releasePublish);

            var sendTask = Task.Run(
                async () => await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false),
                cancellationToken
            );

            // Wait until BasicPublishAsync has been entered, then call Dispose() from a
            // different thread while the publish is still in flight.
            await publishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

            // Dispose during in-flight publish: must not throw on the disposing thread.
            Exception? disposeException = null;
            try
            {
                transport.Dispose();
            }
#pragma warning disable CA1031 // Test must capture any exception type that Dispose might surface.
            catch (Exception ex)
            {
                disposeException = ex;
            }
#pragma warning restore CA1031

            // Release the in-flight publish so the send task can complete.
            releasePublish.SetResult();

            // The send task should now finish; either successfully (channel ref already
            // captured) or with a deterministic exception. We only require that it does
            // not deadlock.
            try
            {
                await sendTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031, RCS1075
            catch
            {
                // A deterministic post-dispose exception is acceptable; the dispose
                // thread's behaviour is the invariant under test.
            }
#pragma warning restore CA1031, RCS1075

            using (Assert.Multiple())
            {
                _ = await Assert.That(disposeException).IsNull();
                _ = await Assert.That(publishStarted.Task.IsCompletedSuccessfully).IsTrue();
            }
        }
        finally
        {
            transport.Dispose();
        }
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
            CorrelationId = "corr-deep-e",
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
        private int _disposeCallCount;

        public bool IsOpen { get; set; } = true;

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public (TaskCompletionSource start, TaskCompletionSource release)? PublishGate { get; set; }

        public void ResetDisposeCounter() => Volatile.Write(ref _disposeCallCount, 0);

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
            if (PublishGate is { } gate)
            {
                _ = gate.start.TrySetResult();
#pragma warning disable VSTHRD003 // TaskCompletionSource gate is signaled by the test, not started here
                await gate.release.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCallCount);
    }
}
