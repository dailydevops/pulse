namespace NetEvolve.Pulse.Redis.Idempotency;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="RedisIdempotencyKeyOptions"/> ensuring that
/// <see cref="RedisIdempotencyKeyOptions.TimeToLive"/> is greater than zero and
/// <see cref="RedisIdempotencyKeyOptions.KeyPrefix"/> is not empty or whitespace.
/// </summary>
internal sealed class RedisIdempotencyKeyOptionsValidator : IValidateOptions<RedisIdempotencyKeyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RedisIdempotencyKeyOptions options)
    {
        if (options.TimeToLive <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RedisIdempotencyKeyOptions.TimeToLive)} must be greater than {TimeSpan.Zero}."
            );
        }

        if (string.IsNullOrWhiteSpace(options.KeyPrefix))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RedisIdempotencyKeyOptions.KeyPrefix)} must not be empty or whitespace."
            );
        }

        return ValidateOptionsResult.Success;
    }
}
