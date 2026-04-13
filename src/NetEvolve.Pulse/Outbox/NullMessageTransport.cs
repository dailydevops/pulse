namespace NetEvolve.Pulse.Outbox;

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
    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Task.CompletedTask;
    }
}
