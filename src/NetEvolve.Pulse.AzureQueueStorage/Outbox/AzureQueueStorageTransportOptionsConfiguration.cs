namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds the <c>Pulse:Transports:AzureQueueStorage</c> configuration section
/// to <see cref="AzureQueueStorageTransportOptions"/>.
/// </summary>
internal sealed class AzureQueueStorageTransportOptionsConfiguration
    : IConfigureOptions<AzureQueueStorageTransportOptions>
{
    private const string ConfigurationSection = "Pulse:Transports:AzureQueueStorage";

    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureQueueStorageTransportOptionsConfiguration"/> class.
    /// </summary>
    /// <param name="configuration">The configuration root.</param>
    public AzureQueueStorageTransportOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(AzureQueueStorageTransportOptions options) =>
        _configuration.GetSection(ConfigurationSection).Bind(options);
}
