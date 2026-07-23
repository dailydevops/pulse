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
/// in particular <see cref="IMessageTransport.SendBatchAsync"/>, which fans out to
/// <see cref="IMessageTransport.SendAsync"/> concurrently via <see cref="Parallel.ForEachAsync{TSource}(System.Collections.Generic.IEnumerable{TSource}, System.Threading.CancellationToken, System.Func{TSource, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask})"/>
/// for implementations that do not override the batch method.
/// </summary>
[TestGroup("Outbox")]
public sealed class IMessageTransportTests
{
    [Test]
    public async Task SendBatchAsync_WithoutOverride_SendsEveryMessageIndividually()
    {
        var transport = new SendAsyncOnlyTransport();
        var messages = new List<OutboxMessage>
        {
            CreateMessage(),
            CreateMessage(),
            CreateMessage(),
            CreateMessage(),
            CreateMessage(),
        };

        await ((IMessageTransport)transport).SendBatchAsync(messages, CancellationToken.None).ConfigureAwait(false);

        _ = await Assert.That(transport.SentMessages.Count).IsEqualTo(messages.Count);
        foreach (var message in messages)
        {
            _ = await Assert.That(transport.SentMessages).Contains(message);
        }
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
    /// Minimal transport that implements only <see cref="IMessageTransport.SendAsync"/>, leaving
    /// <see cref="IMessageTransport.SendBatchAsync"/> to fall through to the default interface
    /// implementation, which dispatches concurrently via <see cref="Parallel.ForEachAsync{TSource}(System.Collections.Generic.IEnumerable{TSource}, System.Threading.CancellationToken, System.Func{TSource, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask})"/>.
    /// </summary>
    private sealed class SendAsyncOnlyTransport : IMessageTransport
    {
        private readonly ConcurrentBag<OutboxMessage> _sentMessages = [];

        public ConcurrentBag<OutboxMessage> SentMessages => _sentMessages;

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            _sentMessages.Add(message);
            return Task.CompletedTask;
        }
    }
}
