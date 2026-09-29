namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="AzureQueueStorageTransportOptions"/> at application startup.
/// </summary>
/// <remarks>
/// The ranges follow the Put Message operation of Azure Queue Storage, which uses whole seconds: the visibility
/// timeout must be between zero and 7 days and smaller than a finite time-to-live (the service default of 7 days
/// when none is set), and the time-to-live must be between one second and <see cref="int.MaxValue"/> seconds or
/// never expire.
/// </remarks>
internal sealed class AzureQueueStorageTransportOptionsValidator : IValidateOptions<AzureQueueStorageTransportOptions>
{
    private static readonly TimeSpan MaxVisibilityTimeout = TimeSpan.FromDays(7);
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromDays(7);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AzureQueueStorageTransportOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString) && options.QueueServiceUri is null)
        {
            failures.Add(
                $"Either {nameof(AzureQueueStorageTransportOptions.ConnectionString)} or {nameof(AzureQueueStorageTransportOptions.QueueServiceUri)} must be provided."
            );
        }

        if (string.IsNullOrWhiteSpace(options.QueueName))
        {
            failures.Add($"{nameof(AzureQueueStorageTransportOptions.QueueName)} must not be empty.");
        }

        var visibilityTimeout = options.MessageVisibilityTimeout;
        if (visibilityTimeout < TimeSpan.Zero || visibilityTimeout > MaxVisibilityTimeout)
        {
            failures.Add(
                $"{nameof(AzureQueueStorageTransportOptions.MessageVisibilityTimeout)} must be between {TimeSpan.Zero} and {MaxVisibilityTimeout}, but was {visibilityTimeout}."
            );
        }

        // The service receives whole seconds, so validate the truncated values that are sent.
        // Without an explicit time-to-live, the service default of 7 days applies.
        var timeToLive = options.MessageTimeToLive;
        long? timeToLiveSeconds = timeToLive switch
        {
            null => (long)DefaultTimeToLive.TotalSeconds,
            { } value when AzureQueueStorageTransportOptions.IsNeverExpires(value) => null,
            { } value => (long)value.TotalSeconds,
        };

        if (timeToLiveSeconds is < 1 or > int.MaxValue)
        {
            failures.Add(
                $"{nameof(AzureQueueStorageTransportOptions.MessageTimeToLive)} must be between one second and {TimeSpan.FromSeconds(int.MaxValue)}, or {nameof(AzureQueueStorageTransportOptions.NeverExpires)} for messages that never expire, but was {timeToLive}."
            );
        }
        else if (
            timeToLiveSeconds is { } ttlSeconds
            && visibilityTimeout is { } visibility
            && visibility <= MaxVisibilityTimeout
            && (long)visibility.TotalSeconds >= ttlSeconds
        )
        {
            failures.Add(
                $"{nameof(AzureQueueStorageTransportOptions.MessageVisibilityTimeout)} ({visibility}) must be smaller than {nameof(AzureQueueStorageTransportOptions.MessageTimeToLive)} ({timeToLive?.ToString() ?? $"service default of {DefaultTimeToLive}"})."
            );
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
