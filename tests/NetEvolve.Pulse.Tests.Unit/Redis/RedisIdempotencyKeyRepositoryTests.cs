namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("Redis")]
public sealed class RedisIdempotencyKeyRepositoryTests
{
    [Test]
    public async Task Constructor_WithNullMultiplexer_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new RedisIdempotencyKeyRepository(null!, Options.Create(new IdempotencyKeyOptions())))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new RedisIdempotencyKeyRepository(Mock.Of<StackExchange.Redis.IConnectionMultiplexer>().Object, null!)
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        var multiplexer = Mock.Of<StackExchange.Redis.IConnectionMultiplexer>().Object;
        var options = Options.Create(new IdempotencyKeyOptions());

        var repository = new RedisIdempotencyKeyRepository(multiplexer, options);

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task ExistsAsync_WithNullKey_ThrowsArgumentException() =>
        _ = await Assert
            .That(async () =>
                await new RedisIdempotencyKeyRepository(
                    Mock.Of<StackExchange.Redis.IConnectionMultiplexer>().Object,
                    Options.Create(new IdempotencyKeyOptions())
                ).ExistsAsync(null!)
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task ExistsAsync_WithEmptyKey_ThrowsArgumentException() =>
        _ = await Assert
            .That(async () =>
                await new RedisIdempotencyKeyRepository(
                    Mock.Of<StackExchange.Redis.IConnectionMultiplexer>().Object,
                    Options.Create(new IdempotencyKeyOptions())
                ).ExistsAsync(string.Empty)
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task ExistsAsync_WithWhitespaceKey_ThrowsArgumentException() =>
        _ = await Assert
            .That(async () =>
                await new RedisIdempotencyKeyRepository(
                    Mock.Of<StackExchange.Redis.IConnectionMultiplexer>().Object,
                    Options.Create(new IdempotencyKeyOptions())
                ).ExistsAsync("   ")
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task StoreAsync_WithNullKey_ThrowsArgumentException() =>
        _ = await Assert
            .That(async () =>
                await new RedisIdempotencyKeyRepository(
                    Mock.Of<StackExchange.Redis.IConnectionMultiplexer>().Object,
                    Options.Create(new IdempotencyKeyOptions())
                ).StoreAsync(null!, DateTimeOffset.UtcNow)
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task StoreAsync_WithEmptyKey_ThrowsArgumentException() =>
        _ = await Assert
            .That(async () =>
                await new RedisIdempotencyKeyRepository(
                    Mock.Of<StackExchange.Redis.IConnectionMultiplexer>().Object,
                    Options.Create(new IdempotencyKeyOptions())
                ).StoreAsync(string.Empty, DateTimeOffset.UtcNow)
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task StoreAsync_WithWhitespaceKey_ThrowsArgumentException() =>
        _ = await Assert
            .That(async () =>
                await new RedisIdempotencyKeyRepository(
                    Mock.Of<StackExchange.Redis.IConnectionMultiplexer>().Object,
                    Options.Create(new IdempotencyKeyOptions())
                ).StoreAsync("   ", DateTimeOffset.UtcNow)
            )
            .Throws<ArgumentException>();
}
