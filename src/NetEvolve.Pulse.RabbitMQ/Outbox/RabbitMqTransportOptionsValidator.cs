namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="RabbitMqTransportOptions"/> ensuring that
/// <see cref="RabbitMqTransportOptions.ExchangeName"/> is not empty and
/// <see cref="RabbitMqTransportOptions.MaxChannelPoolSize"/> is at least 1.
/// </summary>
internal sealed class RabbitMqTransportOptionsValidator : IValidateOptions<RabbitMqTransportOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RabbitMqTransportOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ExchangeName))
        {
            failures.Add($"{nameof(RabbitMqTransportOptions.ExchangeName)} must not be null or empty.");
        }

        if (options.MaxChannelPoolSize < 1)
        {
            failures.Add($"{nameof(RabbitMqTransportOptions.MaxChannelPoolSize)} must be greater than or equal to 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
