namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
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
/// Concurrency invariant tests for <see cref="RabbitMqMessageTransport.Dispose"/>. The
/// transport no longer owns a channel directly (channels are rented from/returned to a
/// pooled <see cref="IRabbitMqChannelPool"/> that is disposed independently via DI), so
/// disposal itself only needs to flip a single-shot sentinel safely under concurrency.
/// </summary>
[TestGroup("RabbitMQ")]
[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Transport disposal is explicitly exercised (and asserted on) inside the test bodies as part of the assertion under test."
)]
public sealed class RabbitMqMessageTransportDisposeConcurrencyTests
{
    /// <summary>
    /// INVARIANT (concurrency / disposal): two threads calling <c>Dispose()</c> at the
    /// same time must not throw and must leave the transport in the disposed state.
    /// </summary>
    [Test]
    public async Task Dispose_CalledConcurrently_DoesNotThrowAndTransportStaysDisposed(
        CancellationToken cancellationToken
    )
    {
        var channelPool = new NoOpChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(channelPool, topicNameResolver);

        var t1 = Task.Run(transport.Dispose, cancellationToken);
        var t2 = Task.Run(transport.Dispose, cancellationToken);
        await Task.WhenAll(t1, t2).ConfigureAwait(false);

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            transport.SendAsync(CreateOutboxMessage(), cancellationToken)
        );

        _ = await Assert.That(exception).IsNotNull();
    }

    /// <summary>
    /// INVARIANT (concurrency / disposal): calling <c>Dispose()</c> while a
    /// <c>SendAsync</c> call is in flight (renting/publishing/returning against the pool)
    /// must not throw on the disposing thread, and the in-flight send must still observe
    /// the publish gate it was waiting for.
    /// </summary>
    [Test]
    public async Task Dispose_DuringInFlightSendAsync_DoesNotThrow(CancellationToken cancellationToken)
    {
        var channelPool = new NoOpChannelPool();
        var topicNameResolver = new FakeTopicNameResolver();
        var transport = CreateTransport(channelPool, topicNameResolver);

        var publishStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channelPool.PublishGate = (start: publishStarted, release: releasePublish);

        var sendTask = Task.Run(
            async () => await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false),
            cancellationToken
        );

        await publishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

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

        releasePublish.SetResult();

        try
        {
            await sendTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031, RCS1075
        catch
        {
            // A deterministic post-dispose exception is acceptable; only the dispose
            // thread's own behavior is under test here.
        }
#pragma warning restore CA1031, RCS1075

        _ = await Assert.That(disposeException).IsNull();
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

    private sealed class NoOpChannelPool : IRabbitMqChannelPool
    {
        public (TaskCompletionSource start, TaskCompletionSource release)? PublishGate { get; set; }

        public ValueTask<IRabbitMqChannelAdapter> RentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IRabbitMqChannelAdapter>(new FakeChannelAdapter(PublishGate));

        public void Return(IRabbitMqChannelAdapter channel) { }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FakeChannelAdapter((TaskCompletionSource start, TaskCompletionSource release)? publishGate)
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
            where TProperties : global::RabbitMQ.Client.IReadOnlyBasicProperties, global::RabbitMQ.Client.IAmqpHeader
        {
            if (publishGate is { } gate)
            {
                _ = gate.start.TrySetResult();
#pragma warning disable VSTHRD003 // TaskCompletionSource gate is signaled by the test, not started here
                await gate.release.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
        }

        public void Dispose() { }
    }
}
