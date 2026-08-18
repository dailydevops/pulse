namespace NetEvolve.Pulse.Outbox;

using System.Diagnostics.CodeAnalysis;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// A no-op <see cref="IMessageTransport"/> implementation that silently discards every message.
/// </summary>
/// <remarks>
/// <para><strong>Use Case:</strong></para>
/// Registered as the default transport by <c>AddOutbox</c>.
/// Replace it by calling <see cref="OutboxExtensions.UseMessageTransport{TTransport}"/> with a concrete
/// transport such as the Dapr or RabbitMQ transport.
/// </remarks>
internal sealed class NullMessageTransport : IMessageTransport
{
    /// <inheritdoc/>
    [SuppressMessage(
        "Usage",
        "NE0009:Method or local function has a CancellationToken parameter but does not check for cancellation at the start of its body",
        Justification = "This transport silently discards every message and does no work; honoring cancellation would contradict that contract and make shutdown behavior depend on transport choice."
    )]
    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Task.CompletedTask;
    }
}
