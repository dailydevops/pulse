namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkIdempotencyKeyRepositoryTests
{
    private static TestIdempotencyDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestIdempotencyDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new TestIdempotencyDbContext(options);
    }

    private static EntityFrameworkIdempotencyKeyRepository<TestIdempotencyDbContext> CreateRepository(
        TestIdempotencyDbContext context
    ) => new(context);

    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new EntityFrameworkIdempotencyKeyRepository<TestIdempotencyDbContext>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidContext_CreatesInstance()
    {
        var context = CreateContext(nameof(Constructor_WithValidContext_CreatesInstance));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);

            _ = await Assert.That(repository).IsNotNull();
        }
    }

    [Test]
    public async Task ExistsAsync_WhenKeyDoesNotExist_ReturnsFalse(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(ExistsAsync_WhenKeyDoesNotExist_ReturnsFalse));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);

            var result = await repository
                .ExistsAsync("non-existent-key", null, cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsFalse();
        }
    }

    [Test]
    public async Task ExistsAsync_WhenKeyExists_ReturnsTrue(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(ExistsAsync_WhenKeyExists_ReturnsTrue));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);
            var now = DateTimeOffset.UtcNow;
            await repository.StoreAsync("existing-key", now, cancellationToken).ConfigureAwait(false);

            var result = await repository.ExistsAsync("existing-key", null, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsTrue();
        }
    }

    [Test]
    public async Task ExistsAsync_WithNullKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(ExistsAsync_WithNullKey_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);

            _ = await Assert
                .That(async () => await repository.ExistsAsync(null!, null, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task ExistsAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(ExistsAsync_WithEmptyKey_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);

            _ = await Assert
                .That(async () =>
                    await repository.ExistsAsync(string.Empty, null, cancellationToken).ConfigureAwait(false)
                )
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task ExistsAsync_WhenKeyExistsAndValidFromIsBeforeCreatedAt_ReturnsTrue(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(nameof(ExistsAsync_WhenKeyExistsAndValidFromIsBeforeCreatedAt_ReturnsTrue));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);
            var createdAt = DateTimeOffset.UtcNow;
            await repository.StoreAsync("valid-key", createdAt, cancellationToken).ConfigureAwait(false);

            // validFrom older than createdAt — key is within TTL
            var validFrom = createdAt.AddMinutes(-5);
            var result = await repository.ExistsAsync("valid-key", validFrom, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsTrue();
        }
    }

    [Test]
    public async Task ExistsAsync_WhenKeyExistsButValidFromIsAfterCreatedAt_ReturnsFalse(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(nameof(ExistsAsync_WhenKeyExistsButValidFromIsAfterCreatedAt_ReturnsFalse));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);
            var createdAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            await repository.StoreAsync("expired-key", createdAt, cancellationToken).ConfigureAwait(false);

            // validFrom newer than createdAt — key is outside TTL
            var validFrom = createdAt.AddMinutes(15);
            var result = await repository
                .ExistsAsync("expired-key", validFrom, cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsFalse();
        }
    }

    [Test]
    public async Task StoreAsync_WithValidKey_StoresKey(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WithValidKey_StoresKey));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);
            var now = DateTimeOffset.UtcNow;

            await repository.StoreAsync("new-key", now, cancellationToken).ConfigureAwait(false);

            var result = await repository.ExistsAsync("new-key", null, cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(result).IsTrue();
        }
    }

    [Test]
    public async Task StoreAsync_WithNullKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WithNullKey_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);

            _ = await Assert
                .That(async () =>
                    await repository.StoreAsync(null!, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false)
                )
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task StoreAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WithEmptyKey_ThrowsArgumentException));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);

            _ = await Assert
                .That(async () =>
                    await repository
                        .StoreAsync(string.Empty, DateTimeOffset.UtcNow, cancellationToken)
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task StoreAsync_WhenCalledTwiceWithSameKey_DoesNotThrow(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(StoreAsync_WhenCalledTwiceWithSameKey_DoesNotThrow));
        await using (context.ConfigureAwait(false))
        {
            var repository = CreateRepository(context);
            var now = DateTimeOffset.UtcNow;

            await repository.StoreAsync("duplicate-key", now, cancellationToken).ConfigureAwait(false);

            // Second store with the same key should NOT throw (idempotent)
            _ = await Assert
                .That(async () =>
                    await repository.StoreAsync("duplicate-key", now, cancellationToken).ConfigureAwait(false)
                )
                .ThrowsNothing();
        }
    }
}
