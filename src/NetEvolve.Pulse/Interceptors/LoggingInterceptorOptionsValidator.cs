namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="LoggingInterceptorOptions"/> ensuring that
/// <see cref="LoggingInterceptorOptions.SlowRequestThreshold"/> is either <see langword="null"/>
/// or a non-negative <see cref="TimeSpan"/>.
/// </summary>
internal sealed class LoggingInterceptorOptionsValidator : IValidateOptions<LoggingInterceptorOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, LoggingInterceptorOptions options)
    {
        if (options.SlowRequestThreshold.HasValue && options.SlowRequestThreshold.Value < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(LoggingInterceptorOptions.SlowRequestThreshold)} must be null or a non-negative {nameof(TimeSpan)}."
            );
        }

        return ValidateOptionsResult.Success;
    }
}
