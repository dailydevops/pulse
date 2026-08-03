namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="RedisStreamsTransportOptions"/> from the <c>Pulse:Transports:RedisStreams</c> configuration section.
/// </summary>
internal sealed class RedisStreamsTransportOptionsConfiguration : IConfigureOptions<RedisStreamsTransportOptions>
{
    private readonly IConfiguration _configuration;

    public RedisStreamsTransportOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(RedisStreamsTransportOptions options) =>
        _configuration.GetSection("Pulse:Transports:RedisStreams").Bind(options);
}
