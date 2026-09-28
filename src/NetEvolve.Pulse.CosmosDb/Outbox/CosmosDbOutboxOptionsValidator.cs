namespace NetEvolve.Pulse.Outbox;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="CosmosDbOutboxOptions"/> at application startup.
/// </summary>
internal sealed class CosmosDbOutboxOptionsValidator : IValidateOptions<CosmosDbOutboxOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CosmosDbOutboxOptions options) =>
        (options.PartitionKeyPathError ?? options.ProcessingLeaseTimeoutError) is { } error
            ? ValidateOptionsResult.Fail(error)
            : ValidateOptionsResult.Success;
}
