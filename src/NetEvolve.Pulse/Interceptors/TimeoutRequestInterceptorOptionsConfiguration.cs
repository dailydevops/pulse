namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="TimeoutRequestInterceptorOptions"/> from the <c>Pulse:Timeout</c> configuration section.
/// </summary>
internal sealed class TimeoutRequestInterceptorOptionsConfiguration
    : IConfigureOptions<TimeoutRequestInterceptorOptions>
{
    private readonly IConfiguration _configuration;

    public TimeoutRequestInterceptorOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(TimeoutRequestInterceptorOptions options) =>
        _configuration.GetSection("Pulse:Timeout").Bind(options);
}
