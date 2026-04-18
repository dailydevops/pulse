namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="AzureQueueStorageTransportOptions"/> at application startup.
/// </summary>
internal sealed class AzureQueueStorageTransportOptionsValidator : IValidateOptions<AzureQueueStorageTransportOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AzureQueueStorageTransportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString) && options.QueueServiceUri is null)
        {
            return ValidateOptionsResult.Fail(
                $"Either {nameof(AzureQueueStorageTransportOptions.ConnectionString)} or {nameof(AzureQueueStorageTransportOptions.QueueServiceUri)} must be provided."
            );
        }

        if (string.IsNullOrWhiteSpace(options.QueueName))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(AzureQueueStorageTransportOptions.QueueName)} must not be empty."
            );
        }

        return ValidateOptionsResult.Success;
    }
}
