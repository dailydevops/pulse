namespace NetEvolve.Pulse.Redis.Idempotency;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="RedisIdempotencyKeyOptions"/> from the <c>Pulse:Idempotency:Redis</c>
/// configuration section.
/// </summary>
internal sealed class RedisIdempotencyKeyOptionsConfiguration : IConfigureOptions<RedisIdempotencyKeyOptions>
{
    private const string SectionPath = "Pulse:Idempotency:Redis";

    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisIdempotencyKeyOptionsConfiguration"/> class.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    public RedisIdempotencyKeyOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(RedisIdempotencyKeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _configuration.GetSection(SectionPath).Bind(options);
    }
}
