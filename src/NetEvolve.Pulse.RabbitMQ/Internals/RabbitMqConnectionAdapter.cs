namespace NetEvolve.Pulse.Internals;

using RabbitMQ.Client;

/// <summary>
/// Adapter implementation that wraps RabbitMQ.Client IConnection.
/// </summary>
internal sealed class RabbitMqConnectionAdapter : IRabbitMqConnectionAdapter
{
    private readonly IConnection _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqConnectionAdapter"/> class.
    /// </summary>
    /// <param name="connection">The underlying RabbitMQ connection.</param>
    public RabbitMqConnectionAdapter(IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <inheritdoc />
    public bool IsOpen => _connection.IsOpen;

    /// <inheritdoc />
    public async Task<IRabbitMqChannelAdapter> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new RabbitMqChannelAdapter(channel);
    }
}
