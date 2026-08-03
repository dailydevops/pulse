namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="AzureServiceBusTransportOptions"/> ensuring that either
/// <see cref="AzureServiceBusTransportOptions.ConnectionString"/> or
/// <see cref="AzureServiceBusTransportOptions.FullyQualifiedNamespace"/> is provided.
/// </summary>
internal sealed class AzureServiceBusTransportOptionsValidator : IValidateOptions<AzureServiceBusTransportOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AzureServiceBusTransportOptions options)
    {
        if (
            string.IsNullOrWhiteSpace(options.ConnectionString)
            && string.IsNullOrWhiteSpace(options.FullyQualifiedNamespace)
        )
        {
            return ValidateOptionsResult.Fail(
                $"Either {nameof(AzureServiceBusTransportOptions.ConnectionString)} or {nameof(AzureServiceBusTransportOptions.FullyQualifiedNamespace)} must be provided."
            );
        }

        return ValidateOptionsResult.Success;
    }
}
