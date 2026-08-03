namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="OutboxOptions"/> from the <c>Pulse:Outbox</c> configuration section.
/// </summary>
internal sealed class OutboxOptionsConfiguration : IConfigureOptions<OutboxOptions>
{
    private readonly IConfiguration _configuration;

    public OutboxOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(OutboxOptions options) => _configuration.GetSection("Pulse:Outbox").Bind(options);
}
