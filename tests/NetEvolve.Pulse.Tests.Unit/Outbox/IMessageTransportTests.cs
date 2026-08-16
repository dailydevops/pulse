namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Unit tests for the default interface implementations on <see cref="IMessageTransport"/>,
/// in particular <see cref="IMessageTransport.SendBatchAsync"/>, which calls
/// <see cref="IMessageTransport.SendAsync"/> for each message sequentially, as documented, and fails fast
/// for implementations that do not override the batch method.
/// </summary>
[TestGroup("Outbox")]
public sealed class IMessageTransportTests
{
    [Test]
    public async Task SendBatchAsync_WithoutOverride_SendsEveryMessageIndividually(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var transport = new SendAsyncOnlyTransport();
        var messages = new List<OutboxMessage>
        {
            CreateMessage(),
            CreateMessage(),
            CreateMessage(),
            CreateMessage(),
            CreateMessage(),
        };

        await ((IMessageTransport)transport).SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(transport.SentMessages.Count).IsEqualTo(messages.Count);
        foreach (var message in messages)
        {
            _ = await Assert.That(transport.SentMessages).Contains(message);
        }
    }

    [Test]
    public async Task SendBatchAsync_WithoutOverride_SendsMessagesSequentiallyInOrder(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var transport = new SequenceRecordingTransport();
        var messages = new List<OutboxMessage> { CreateMessage(), CreateMessage(), CreateMessage(), CreateMessage() };

        await ((IMessageTransport)transport).SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(transport.OverlapDetected).IsFalse();
        _ = await Assert.That(transport.SendOrder.Count).IsEqualTo(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            _ = await Assert.That(transport.SendOrder[i]).IsEqualTo(messages[i].Id);
        }
    }

    [Test]
    public async Task SendBatchAsync_WithoutOverride_WhenSendFails_StopsAtFailedMessage()
    {
        var transport = new SequentialFailingTransport(failAtCallNumber: 2);
        var messages = new List<OutboxMessage> { CreateMessage(), CreateMessage(), CreateMessage(), CreateMessage() };

        _ = await Assert
            .That(() => ((IMessageTransport)transport).SendBatchAsync(messages, CancellationToken.None))
            .Throws<InvalidOperationException>();

        _ = await Assert.That(transport.AttemptedSends).IsEqualTo(2);
    }

    [Test]
    public async Task SendBatchAsync_WithoutOverride_WithNullMessages_ThrowsArgumentNullException()
    {
        var transport = new SendAsyncOnlyTransport();

        _ = await Assert
            .That(() => ((IMessageTransport)transport).SendBatchAsync(null!, CancellationToken.None))
            .Throws<ArgumentNullException>();
    }

    private static OutboxMessage CreateMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(object),
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };

    /// <summary>
    /// Transport implementing only <see cref="IMessageTransport.SendAsync"/> that records the
    /// invocation order and detects overlapping (concurrent) invocations. The first send waits briefly
    /// for a subsequent send to start; if one starts before the first completes, the default batch
    /// dispatch is concurrent and violates the documented sequential contract of
    /// <see cref="IMessageTransport.SendBatchAsync"/>.
    /// </summary>
    private sealed class SequenceRecordingTransport : IMessageTransport, IDisposable
    {
        private readonly SemaphoreSlim _subsequentSendStarted = new(0);
        private int _activeSends;
        private int _totalSends;

        public bool OverlapDetected { get; private set; }

        public List<Guid> SendOrder { get; } = [];

        public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Interlocked.Increment(ref _activeSends) > 1)
            {
                OverlapDetected = true;
            }

            lock (SendOrder)
            {
                SendOrder.Add(message.Id);
            }

            if (Interlocked.Increment(ref _totalSends) == 1)
            {
                _ = await _subsequentSendStarted.WaitAsync(500, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _ = _subsequentSendStarted.Release();
            }

            _ = Interlocked.Decrement(ref _activeSends);
        }

        public void Dispose() => _subsequentSendStarted.Dispose();
    }

    /// <summary>
    /// Transport implementing only <see cref="IMessageTransport.SendAsync"/> that throws on a
    /// configurable invocation, used to verify fail-fast semantics of the default batch dispatch.
    /// </summary>
    private sealed class SequentialFailingTransport(int failAtCallNumber) : IMessageTransport
    {
        private int _attemptedSends;

        public int AttemptedSends => _attemptedSends;

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var callNumber = Interlocked.Increment(ref _attemptedSends);
            return callNumber == failAtCallNumber
                ? Task.FromException(new InvalidOperationException("Send failed."))
                : Task.CompletedTask;
        }
    }

    /// <summary>
    /// Minimal transport that implements only <see cref="IMessageTransport.SendAsync"/>, leaving
    /// <see cref="IMessageTransport.SendBatchAsync"/> to fall through to the default interface
    /// implementation, which dispatches each message sequentially.
    /// </summary>
    private sealed class SendAsyncOnlyTransport : IMessageTransport
    {
        private readonly ConcurrentBag<OutboxMessage> _sentMessages = [];

        public ConcurrentBag<OutboxMessage> SentMessages => _sentMessages;

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _sentMessages.Add(message);
            return Task.CompletedTask;
        }
    }
}
