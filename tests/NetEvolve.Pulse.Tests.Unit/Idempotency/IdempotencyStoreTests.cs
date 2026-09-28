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
    ) =>
        new IdempotencyStore(
            repository,
            Options.Create(options ?? new IdempotencyKeyOptions()),
            timeProvider ?? TimeProvider.System
        );

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
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore(new TrackingIdempotencyKeyRepository());

        _ = await Assert
            .That(async () => await store.ExistsAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ExistsAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore(new TrackingIdempotencyKeyRepository());

        _ = await Assert
            .That(async () => await store.ExistsAsync(string.Empty, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ExistsAsync_WithoutTtl_PassesNullCutoffToRepository(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new TrackingIdempotencyKeyRepository();
        var store = CreateStore(repository, new IdempotencyKeyOptions { TimeToLive = null });

        _ = await store.ExistsAsync("test-key", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.CapturedValidFrom).IsNull();
    }

    [Test]
    public async Task ExistsAsync_WithTtl_PassesCutoffToRepository(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore(new TrackingIdempotencyKeyRepository());

        _ = await Assert
            .That(async () => await store.StoreAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore(new TrackingIdempotencyKeyRepository());

        _ = await Assert
            .That(async () => await store.StoreAsync(string.Empty, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task StoreAsync_PassesCurrentTimestampToRepository(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fakeTime = new FakeTimeProvider();
        var expectedTimestamp = fakeTime.GetUtcNow();

        var repository = new TrackingIdempotencyKeyRepository();
        var store = CreateStore(repository, timeProvider: fakeTime);

        await store.StoreAsync("test-key", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.CapturedCreatedAt).IsEqualTo(expectedTimestamp);
    }

    [Test]
    public async Task TryReserveAsync_WithTtl_ReservesAtomicallyWithCutoff(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fakeTime = new FakeTimeProvider();
        var now = fakeTime.GetUtcNow();
        var ttl = TimeSpan.FromMinutes(10);

        var repository = new TrackingIdempotencyKeyRepository();
        var store = CreateStore(repository, new IdempotencyKeyOptions { TimeToLive = ttl }, fakeTime);

        var result = await ((IIdempotencyStore)store)
            .TryReserveAsync("test-key", cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsTrue();
            _ = await Assert.That(repository.ReserveCount).IsEqualTo(1);
            _ = await Assert.That(repository.CapturedCreatedAt).IsEqualTo(now);
            _ = await Assert.That(repository.CapturedValidFrom).IsEqualTo(now - ttl);
        }
    }

    [Test]
    public async Task TryReserveAsync_WithoutTtl_ReservesWithoutCutoff(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new TrackingIdempotencyKeyRepository();
        var store = CreateStore(repository, new IdempotencyKeyOptions { TimeToLive = null });

        _ = await ((IIdempotencyStore)store).TryReserveAsync("test-key", cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(repository.ReserveCount).IsEqualTo(1);
            _ = await Assert.That(repository.CapturedValidFrom).IsNull();
        }
    }

    [Test]
    public async Task TryReserveAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore(new TrackingIdempotencyKeyRepository());

        _ = await Assert
            .That(async () =>
                await ((IIdempotencyStore)store).TryReserveAsync(string.Empty, cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task StoreAsync_WithTtl_RefreshesExpiredKeyThroughReserve(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fakeTime = new FakeTimeProvider();
        var now = fakeTime.GetUtcNow();
        var ttl = TimeSpan.FromMinutes(10);

        var repository = new TrackingIdempotencyKeyRepository();
        var store = CreateStore(repository, new IdempotencyKeyOptions { TimeToLive = ttl }, fakeTime);

        await store.StoreAsync("test-key", cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(repository.ReserveCount).IsEqualTo(1);
            _ = await Assert.That(repository.CapturedValidFrom).IsEqualTo(now - ttl);
        }
    }

    private sealed class TrackingIdempotencyKeyRepository : IIdempotencyKeyRepository
    {
        public DateTimeOffset? CapturedValidFrom { get; private set; } = DateTimeOffset.MaxValue;
        public DateTimeOffset CapturedCreatedAt { get; private set; }
        public int ReserveCount { get; private set; }

        public Task<bool> ExistsAsync(
            string idempotencyKey,
            DateTimeOffset? validFrom = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            CapturedValidFrom = validFrom;
            return Task.FromResult(false);
        }

        public Task StoreAsync(
            string idempotencyKey,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            CapturedCreatedAt = createdAt;
            return Task.CompletedTask;
        }

        public Task<bool> TryReserveAsync(
            string idempotencyKey,
            DateTimeOffset createdAt,
            DateTimeOffset? validFrom = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReserveCount++;
            CapturedCreatedAt = createdAt;
            CapturedValidFrom = validFrom;
            return Task.FromResult(true);
        }
    }
}
