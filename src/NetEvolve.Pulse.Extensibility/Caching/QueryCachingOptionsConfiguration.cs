namespace NetEvolve.Pulse.Extensibility.Caching;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds <see cref="QueryCachingOptions"/> from the <c>Pulse:QueryCaching</c> configuration section.
/// </summary>
public sealed class QueryCachingOptionsConfiguration : IConfigureOptions<QueryCachingOptions>
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryCachingOptionsConfiguration"/> class.
    /// </summary>
    /// <param name="configuration">The application configuration used to bind <see cref="QueryCachingOptions"/>.</param>
    public QueryCachingOptionsConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(QueryCachingOptions options) => _configuration.GetSection("Pulse:QueryCaching").Bind(options);
}
