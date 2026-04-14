namespace NetEvolve.Pulse.Tests.Unit.Idempotency;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Idempotency")]
public sealed class IdempotencyStoreTests
{
    private static IdempotencyStore CreateStore(
        IIdempotencyKeyRepository repository,
        IdempotencyKeyOptions? options = null,
        TimeProvider? timeProvider = null
    ) => new(repository, Options.Create(options ?? new IdempotencyKeyOptions()), timeProvider ?? TimeProvider.System);

    [Test]
    public async Task Constructor_WithNullRepository_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new IdempotencyStore(null!, Options.Create(new IdempotencyKeyOptions()), TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var repository = new TrackingIdempotencyKeyRepository();

        _ = await Assert
            .That(() => new IdempotencyStore(repository, null!, TimeProvider.System))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var repository = new TrackingIdempotencyKeyRepository();

        _ = await Assert
            .That(() => new IdempotencyStore(repository, Options.Create(new IdempotencyKeyOptions()), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ExistsAsync_WithNullKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var store = CreateStore(new TrackingIdempotencyKeyRepository());

        _ = await Assert
            .That(async () => await store.ExistsAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ExistsAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var store = CreateStore(new TrackingIdempotencyKeyRepository());

        _ = await Assert
            .That(async () => await store.ExistsAsync(string.Empty, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ExistsAsync_WithoutTtl_PassesNullCutoffToRepository(CancellationToken cancellationToken)
    {
        var repository = new TrackingIdempotencyKeyRepository();
        var store = CreateStore(repository, new IdempotencyKeyOptions { TimeToLive = null });

        _ = await store.ExistsAsync("test-key", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.CapturedValidFrom).IsNull();
    }

    [Test]
    public async Task ExistsAsync_WithTtl_PassesCutoffToRepository(CancellationToken cancellationToken)
    {
        var fakeTime = new FakeTimeProvider();
        var now = fakeTime.GetUtcNow();
        var ttl = TimeSpan.FromMinutes(10);
        var expectedCutoff = now - ttl;

        var repository = new TrackingIdempotencyKeyRepository();
        var store = CreateStore(repository, new IdempotencyKeyOptions { TimeToLive = ttl }, fakeTime);

        _ = await store.ExistsAsync("test-key", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.CapturedValidFrom).IsEqualTo(expectedCutoff);
    }

    [Test]
    public async Task StoreAsync_WithNullKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var store = CreateStore(new TrackingIdempotencyKeyRepository());

        _ = await Assert
            .That(async () => await store.StoreAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var store = CreateStore(new TrackingIdempotencyKeyRepository());

        _ = await Assert
            .That(async () => await store.StoreAsync(string.Empty, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task StoreAsync_PassesCurrentTimestampToRepository(CancellationToken cancellationToken)
    {
        var fakeTime = new FakeTimeProvider();
        var expectedTimestamp = fakeTime.GetUtcNow();

        var repository = new TrackingIdempotencyKeyRepository();
        var store = CreateStore(repository, timeProvider: fakeTime);

        await store.StoreAsync("test-key", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.CapturedCreatedAt).IsEqualTo(expectedTimestamp);
    }

    private sealed class TrackingIdempotencyKeyRepository : IIdempotencyKeyRepository
    {
        public DateTimeOffset? CapturedValidFrom { get; private set; } = DateTimeOffset.MaxValue;
        public DateTimeOffset CapturedCreatedAt { get; private set; }

        public Task<bool> ExistsAsync(
            string idempotencyKey,
            DateTimeOffset? validFrom = null,
            CancellationToken cancellationToken = default
        )
        {
            CapturedValidFrom = validFrom;
            return Task.FromResult(false);
        }

        public Task StoreAsync(
            string idempotencyKey,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken = default
        )
        {
            CapturedCreatedAt = createdAt;
            return Task.CompletedTask;
        }
    }
}
