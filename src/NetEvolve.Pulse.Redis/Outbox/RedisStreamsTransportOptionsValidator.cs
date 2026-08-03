namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="RedisStreamsTransportOptions"/> ensuring that
/// <see cref="RedisStreamsTransportOptions.StreamKey"/> and
/// <see cref="RedisStreamsTransportOptions.ConsumerGroupName"/> are not empty.
/// </summary>
internal sealed class RedisStreamsTransportOptionsValidator : IValidateOptions<RedisStreamsTransportOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RedisStreamsTransportOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.StreamKey))
        {
            failures.Add($"{nameof(RedisStreamsTransportOptions.StreamKey)} must not be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(options.ConsumerGroupName))
        {
            failures.Add($"{nameof(RedisStreamsTransportOptions.ConsumerGroupName)} must not be null or empty.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
