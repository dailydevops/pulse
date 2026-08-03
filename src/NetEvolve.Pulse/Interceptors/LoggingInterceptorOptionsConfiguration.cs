namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="LoggingInterceptorOptions"/> from the <c>Pulse:Logging</c> configuration section.
/// </summary>
internal sealed class LoggingInterceptorOptionsConfiguration : IConfigureOptions<LoggingInterceptorOptions>
{
    private readonly IConfiguration _configuration;

    public LoggingInterceptorOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(LoggingInterceptorOptions options) =>
        _configuration.GetSection("Pulse:Logging").Bind(options);
}
