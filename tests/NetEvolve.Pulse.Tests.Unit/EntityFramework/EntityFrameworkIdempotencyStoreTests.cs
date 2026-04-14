namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkIdempotencyStoreTests
{
    private static TestIdempotencyDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestIdempotencyDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new TestIdempotencyDbContext(options);
    }

    private static EntityFrameworkIdempotencyStore<TestIdempotencyDbContext> CreateStore(
        TestIdempotencyDbContext context,
        IdempotencyKeyOptions? options = null,
        TimeProvider? timeProvider = null
    ) => new(context, Options.Create(options ?? new IdempotencyKeyOptions()), timeProvider ?? TimeProvider.System);

    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new EntityFrameworkIdempotencyStore<TestIdempotencyDbContext>(
                    null!,
                    Options.Create(new IdempotencyKeyOptions()),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var context = CreateContext(nameof(Constructor_WithNullOptions_ThrowsArgumentNullException));
        await using (context.ConfigureAwait(false))
        {
            _ = await Assert
                .That(() =>
                    new EntityFrameworkIdempotencyStore<TestIdempotencyDbContext>(context, null!, TimeProvider.System)
                )
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var context = CreateContext(nameof(Constructor_WithNullTimeProvider_ThrowsArgumentNullException));
        await using (context.ConfigureAwait(false))
        {
            _ = await Assert
                .That(() =>
                    new EntityFrameworkIdempotencyStore<TestIdempotencyDbContext>(
                        context,
                        Options.Create(new IdempotencyKeyOptions()),
                        null!
                    )
                )
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task ExistsAsync_WhenKeyDoesNotExist_ReturnsFalse(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(ExistsAsync_WhenKeyDoesNotExist_ReturnsFalse));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            var result = await store.ExistsAsync("non-existent-key", cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsFalse();
        }
    }

    [Test]
    public async Task ExistsAsync_WhenKeyExists_ReturnsTrue(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(ExistsAsync_WhenKeyExists_ReturnsTrue));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);
            await store.StoreAsync("existing-key", cancellationToken).ConfigureAwait(false);

            var result = await store.ExistsAsync("existing-key", cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsTrue();
        }
    }

    [Test]
    public async Task ExistsAsync_WithNullKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(ExistsAsync_WithNullKey_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert
                .That(async () => await store.ExistsAsync(null!, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task ExistsAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(ExistsAsync_WithEmptyKey_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert
                .That(async () => await store.ExistsAsync(string.Empty, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task ExistsAsync_WithTtl_WhenKeyIsNotExpired_ReturnsTrue(CancellationToken cancellationToken)
    {
        var fakeTime = new FakeTimeProvider();
        var options = new IdempotencyKeyOptions { TimeToLive = TimeSpan.FromMinutes(10) };

        var context = CreateContext(nameof(ExistsAsync_WithTtl_WhenKeyIsNotExpired_ReturnsTrue));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context, options, fakeTime);
            await store.StoreAsync("ttl-key", cancellationToken).ConfigureAwait(false);

            // Advance time by less than TTL
            fakeTime.Advance(TimeSpan.FromMinutes(5));

            var result = await store.ExistsAsync("ttl-key", cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsTrue();
        }
    }

    [Test]
    public async Task ExistsAsync_WithTtl_WhenKeyIsExpired_ReturnsFalse(CancellationToken cancellationToken)
    {
        var fakeTime = new FakeTimeProvider();
        var options = new IdempotencyKeyOptions { TimeToLive = TimeSpan.FromMinutes(10) };

        var context = CreateContext(nameof(ExistsAsync_WithTtl_WhenKeyIsExpired_ReturnsFalse));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context, options, fakeTime);
            await store.StoreAsync("expiring-key", cancellationToken).ConfigureAwait(false);

            // Advance time past TTL
            fakeTime.Advance(TimeSpan.FromMinutes(15));

            var result = await store.ExistsAsync("expiring-key", cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsFalse();
        }
    }

    [Test]
    public async Task StoreAsync_WithValidKey_StoresKey(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WithValidKey_StoresKey));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            await store.StoreAsync("new-key", cancellationToken).ConfigureAwait(false);

            var result = await store.ExistsAsync("new-key", cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(result).IsTrue();
        }
    }

    [Test]
    public async Task StoreAsync_WithNullKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WithNullKey_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert
                .That(async () => await store.StoreAsync(null!, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task StoreAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WithEmptyKey_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            _ = await Assert
                .That(async () => await store.StoreAsync(string.Empty, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task StoreAsync_WhenCalledTwiceWithSameKey_DoesNotThrow(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WhenCalledTwiceWithSameKey_DoesNotThrow));
        await using (context.ConfigureAwait(false))
        {
            var store = CreateStore(context);

            await store.StoreAsync("duplicate-key", cancellationToken).ConfigureAwait(false);

            // Second store with the same key should NOT throw (idempotent)
            _ = await Assert
                .That(async () => await store.StoreAsync("duplicate-key", cancellationToken).ConfigureAwait(false))
                .ThrowsNothing();
        }
    }
}
