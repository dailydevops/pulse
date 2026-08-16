namespace NetEvolve.Pulse.OutBox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="DaprMessageTransportOptions"/> ensuring that
/// <see cref="DaprMessageTransportOptions.PubSubName"/> is not empty.
/// </summary>
internal sealed class DaprMessageTransportOptionsValidator : IValidateOptions<DaprMessageTransportOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DaprMessageTransportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PubSubName))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(DaprMessageTransportOptions.PubSubName)} must not be null or empty."
            );
        }

        return ValidateOptionsResult.Success;
    }
}
