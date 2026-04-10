namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="NullMessageTransport"/>.
/// Verifies that the no-op transport completes silently for any input.
/// </summary>
[TestGroup("Outbox")]
public sealed class NullMessageTransportTests
{
    [Test]
    public async Task SendAsync_WithValidMessage_CompletesSuccessfully()
    {
        var transport = new NullMessageTransport();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(object),
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };

        await transport.SendAsync(message).ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsync_WithCancelledToken_CompletesSuccessfully()
    {
        var transport = new NullMessageTransport();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(object),
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        await transport.SendAsync(message, cts.Token).ConfigureAwait(false);
    }
}
