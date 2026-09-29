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
/// expired keys are logically ignored by passing a cutoff timestamp to the repository, and
/// <see cref="StoreAsync"/> and <see cref="TryReserveAsync"/> refresh the timestamp of an expired key.
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
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        return _repository.ExistsAsync(idempotencyKey, GetCutoff(), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// An expired key is refreshed, so that <see cref="ExistsAsync"/> returns <see langword="true"/> again afterwards.
    /// </remarks>
    public Task StoreAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
        TryReserveAsync(idempotencyKey, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the atomic <see cref="IIdempotencyKeyRepository.TryReserveAsync"/>, which also refreshes
    /// the timestamp of a key that has outlived <see cref="IdempotencyKeyOptions.TimeToLive"/>.
    /// </remarks>
    public Task<bool> TryReserveAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var now = _timeProvider.GetUtcNow();
        return _repository.TryReserveAsync(idempotencyKey, now, GetCutoff(now), cancellationToken);
    }

    private DateTimeOffset? GetCutoff() => GetCutoff(_timeProvider.GetUtcNow());

    private DateTimeOffset? GetCutoff(DateTimeOffset now) =>
        _options.TimeToLive.HasValue ? now - _options.TimeToLive.Value : null;
}
