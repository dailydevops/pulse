namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="TimeoutRequestInterceptorOptions"/> ensuring that
/// <see cref="TimeoutRequestInterceptorOptions.GlobalTimeout"/> is either <see langword="null"/>
/// or a positive <see cref="TimeSpan"/>.
/// </summary>
internal sealed class TimeoutRequestInterceptorOptionsValidator : IValidateOptions<TimeoutRequestInterceptorOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TimeoutRequestInterceptorOptions options)
    {
        if (options.GlobalTimeout.HasValue && options.GlobalTimeout.Value <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TimeoutRequestInterceptorOptions.GlobalTimeout)} must be null or greater than {nameof(TimeSpan)}.{nameof(TimeSpan.Zero)}."
            );
        }

        return ValidateOptionsResult.Success;
    }
}
