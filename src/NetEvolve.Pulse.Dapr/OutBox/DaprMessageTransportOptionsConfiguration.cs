namespace NetEvolve.Pulse.OutBox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="DaprMessageTransportOptions"/> from the <c>Pulse:Transports:Dapr</c> configuration section.
/// </summary>
internal sealed class DaprMessageTransportOptionsConfiguration : IConfigureOptions<DaprMessageTransportOptions>
{
    private readonly IConfiguration _configuration;

    public DaprMessageTransportOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(DaprMessageTransportOptions options) =>
        _configuration.GetSection("Pulse:Transports:Dapr").Bind(options);
}
