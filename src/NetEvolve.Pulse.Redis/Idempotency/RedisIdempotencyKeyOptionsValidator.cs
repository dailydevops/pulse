namespace NetEvolve.Pulse.Idempotency;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="IdempotencyKeyOptions"/> for the Redis idempotency store, ensuring that
/// <see cref="IdempotencyKeyOptions.TableName"/> is not empty and
/// <see cref="IdempotencyKeyOptions.TimeToLive"/>, when set, is positive.
/// </summary>
/// <remarks>
/// <see cref="IdempotencyKeyOptions.Schema"/> is intentionally not validated: the options type is
/// shared with the SQL-based providers, where <see langword="null"/> or empty means "default schema".
/// For Redis such a value simply yields an empty first key segment.
/// </remarks>
internal sealed class RedisIdempotencyKeyOptionsValidator : IValidateOptions<IdempotencyKeyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IdempotencyKeyOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TableName))
        {
            failures.Add($"{nameof(IdempotencyKeyOptions.TableName)} must not be null or empty.");
        }

        if (options.TimeToLive <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(IdempotencyKeyOptions.TimeToLive)} must be greater than zero when set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
