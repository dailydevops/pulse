namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="AzureServiceBusTransportOptions"/> from the <c>Pulse:Transports:AzureServiceBus</c>
/// configuration section.
/// </summary>
internal sealed class AzureServiceBusTransportOptionsConfiguration : IConfigureOptions<AzureServiceBusTransportOptions>
{
    private readonly IConfiguration _configuration;

    public AzureServiceBusTransportOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(AzureServiceBusTransportOptions options) =>
        _configuration.GetSection("Pulse:Transports:AzureServiceBus").Bind(options);
}
