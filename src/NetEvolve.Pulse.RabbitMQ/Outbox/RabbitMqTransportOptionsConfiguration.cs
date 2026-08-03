namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="RabbitMqTransportOptions"/> from the <c>Pulse:Transports:RabbitMq</c> configuration section.
/// </summary>
internal sealed class RabbitMqTransportOptionsConfiguration : IConfigureOptions<RabbitMqTransportOptions>
{
    private readonly IConfiguration _configuration;

    public RabbitMqTransportOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(RabbitMqTransportOptions options) =>
        _configuration.GetSection("Pulse:Transports:RabbitMq").Bind(options);
}
