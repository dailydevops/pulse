namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="AzureQueueStorageTransportOptions"/> at application startup.
/// </summary>
/// <remarks>
/// The ranges follow the Put Message operation of Azure Queue Storage, which uses whole seconds: the visibility
/// timeout must be between zero and 7 days and smaller than a finite time-to-live, and the time-to-live must be
/// at least one second or never expire.
/// </remarks>
internal sealed class AzureQueueStorageTransportOptionsValidator : IValidateOptions<AzureQueueStorageTransportOptions>
{
    private static readonly TimeSpan MaxVisibilityTimeout = TimeSpan.FromDays(7);

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

        if (options.MessageTimeToLive is { } timeToLive && !IsNeverExpires(timeToLive))
        {
            // The service receives whole seconds, so validate the truncated values that are sent.
            var timeToLiveSeconds = (long)timeToLive.TotalSeconds;
            if (timeToLiveSeconds < 1)
            {
                failures.Add(
                    $"{nameof(AzureQueueStorageTransportOptions.MessageTimeToLive)} must be at least one second or {nameof(AzureQueueStorageTransportOptions.NeverExpires)}, but was {timeToLive}."
                );
            }
            else if (visibilityTimeout is { } visibility && (long)visibility.TotalSeconds >= timeToLiveSeconds)
            {
                failures.Add(
                    $"{nameof(AzureQueueStorageTransportOptions.MessageVisibilityTimeout)} ({visibility}) must be smaller than {nameof(AzureQueueStorageTransportOptions.MessageTimeToLive)} ({timeToLive})."
                );
            }
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Determines whether <paramref name="timeToLive"/> requests messages that never expire.
    /// </summary>
    /// <param name="timeToLive">The configured time-to-live.</param>
    /// <returns>
    /// <see langword="true"/> for <see cref="AzureQueueStorageTransportOptions.NeverExpires"/> or
    /// <see cref="Timeout.InfiniteTimeSpan"/>; otherwise, <see langword="false"/>.
    /// </returns>
    internal static bool IsNeverExpires(TimeSpan timeToLive) =>
        timeToLive == AzureQueueStorageTransportOptions.NeverExpires || timeToLive == Timeout.InfiniteTimeSpan;
}
