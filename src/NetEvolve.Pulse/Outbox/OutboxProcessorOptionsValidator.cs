namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="OutboxProcessorOptions"/> ensuring all configured values are within
/// valid, self-consistent ranges.
/// </summary>
internal sealed class OutboxProcessorOptionsValidator : IValidateOptions<OutboxProcessorOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OutboxProcessorOptions options)
    {
        var failures = new List<string>();

        if (options.BatchSize <= 0)
        {
            failures.Add($"{nameof(OutboxProcessorOptions.BatchSize)} must be greater than 0.");
        }

        if (options.PollingInterval <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(OutboxProcessorOptions.PollingInterval)} must be greater than {nameof(TimeSpan)}.{nameof(TimeSpan.Zero)}."
            );
        }

        if (options.MaxRetryCount < 0)
        {
            failures.Add($"{nameof(OutboxProcessorOptions.MaxRetryCount)} must be greater than or equal to 0.");
        }

        if (options.ProcessingTimeout <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(OutboxProcessorOptions.ProcessingTimeout)} must be greater than {nameof(TimeSpan)}.{nameof(TimeSpan.Zero)}."
            );
        }

        if (options.EnableExponentialBackoff)
        {
            if (options.BackoffMultiplier <= 1.0)
            {
                failures.Add(
                    $"{nameof(OutboxProcessorOptions.BackoffMultiplier)} must be greater than 1.0 when {nameof(OutboxProcessorOptions.EnableExponentialBackoff)} is enabled."
                );
            }

            if (options.BaseRetryDelay <= TimeSpan.Zero)
            {
                failures.Add(
                    $"{nameof(OutboxProcessorOptions.BaseRetryDelay)} must be greater than {nameof(TimeSpan)}.{nameof(TimeSpan.Zero)} when {nameof(OutboxProcessorOptions.EnableExponentialBackoff)} is enabled."
                );
            }

            if (options.MaxRetryDelay < options.BaseRetryDelay)
            {
                failures.Add(
                    $"{nameof(OutboxProcessorOptions.MaxRetryDelay)} must be greater than or equal to {nameof(OutboxProcessorOptions.BaseRetryDelay)} when {nameof(OutboxProcessorOptions.EnableExponentialBackoff)} is enabled."
                );
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
