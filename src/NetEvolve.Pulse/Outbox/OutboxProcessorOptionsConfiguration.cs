namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="OutboxProcessorOptions"/> from the <c>Pulse:OutboxProcessor</c> configuration section.
/// </summary>
internal sealed class OutboxProcessorOptionsConfiguration : IConfigureOptions<OutboxProcessorOptions>
{
    private readonly IConfiguration _configuration;

    public OutboxProcessorOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(OutboxProcessorOptions options) =>
        _configuration.GetSection("Pulse:OutboxProcessor").Bind(options);
}
