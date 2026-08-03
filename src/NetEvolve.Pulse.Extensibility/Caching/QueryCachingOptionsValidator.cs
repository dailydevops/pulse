namespace NetEvolve.Pulse.Extensibility.Caching;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="QueryCachingOptions"/> ensuring that
/// <see cref="QueryCachingOptions.DefaultExpiry"/> is either <see langword="null"/>
/// or a positive <see cref="TimeSpan"/>.
/// </summary>
public sealed class QueryCachingOptionsValidator : IValidateOptions<QueryCachingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, QueryCachingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DefaultExpiry.HasValue && options.DefaultExpiry.Value <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QueryCachingOptions.DefaultExpiry)} must be null or greater than {nameof(TimeSpan)}.{nameof(TimeSpan.Zero)}."
            );
        }

        return ValidateOptionsResult.Success;
    }
}
