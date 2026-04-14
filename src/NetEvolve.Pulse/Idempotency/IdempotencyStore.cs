namespace NetEvolve.Pulse.Idempotency;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// Central implementation of <see cref="IIdempotencyStore"/> that delegates persistence
/// to the registered <see cref="IIdempotencyKeyRepository"/>.
/// </summary>
/// <remarks>
/// <para><strong>Time-to-Live:</strong></para>
/// When <see cref="IdempotencyKeyOptions.TimeToLive"/> is set, keys older than the TTL
/// are treated as absent by <see cref="ExistsAsync"/>. Physical deletion is not performed;
/// expired keys are logically ignored by passing a cutoff timestamp to the repository.
/// </remarks>
internal sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly IIdempotencyKeyRepository _repository;
    private readonly IdempotencyKeyOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyStore"/> class.
    /// </summary>
    /// <param name="repository">The repository for storing and retrieving idempotency keys.</param>
    /// <param name="options">The idempotency key options.</param>
    /// <param name="timeProvider">The time provider for computing TTL cutoff timestamps.</param>
    public IdempotencyStore(
        IIdempotencyKeyRepository repository,
        IOptions<IdempotencyKeyOptions> options,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        DateTimeOffset? cutoff = _options.TimeToLive.HasValue
            ? _timeProvider.GetUtcNow() - _options.TimeToLive.Value
            : null;

        return _repository.ExistsAsync(idempotencyKey, cutoff, cancellationToken);
    }

    /// <inheritdoc />
    public Task StoreAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        return _repository.StoreAsync(idempotencyKey, _timeProvider.GetUtcNow(), cancellationToken);
    }
}
