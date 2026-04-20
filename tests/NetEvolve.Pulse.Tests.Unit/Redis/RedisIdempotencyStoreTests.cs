namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using StackExchange.Redis;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("Redis")]
public sealed class RedisIdempotencyStoreTests
{
    [Test]
    public async Task Constructor_WithNullMultiplexer_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new RedisIdempotencyStore(null!, Options.Create(new RedisIdempotencyKeyOptions())))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new RedisIdempotencyStore(Mock.Of<IConnectionMultiplexer>().Object, null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var store = new RedisIdempotencyStore(
            Mock.Of<IConnectionMultiplexer>().Object,
            Options.Create(new RedisIdempotencyKeyOptions())
        );

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task ExistsAsync_WithNullKey_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var store = new RedisIdempotencyStore(
            Mock.Of<IConnectionMultiplexer>().Object,
            Options.Create(new RedisIdempotencyKeyOptions())
        );

        _ = await Assert
            .That(async () => await store.ExistsAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ExistsAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var store = new RedisIdempotencyStore(
            Mock.Of<IConnectionMultiplexer>().Object,
            Options.Create(new RedisIdempotencyKeyOptions())
        );

        _ = await Assert
            .That(async () => await store.ExistsAsync(string.Empty, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task StoreAsync_WithNullKey_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var store = new RedisIdempotencyStore(
            Mock.Of<IConnectionMultiplexer>().Object,
            Options.Create(new RedisIdempotencyKeyOptions())
        );

        _ = await Assert
            .That(async () => await store.StoreAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithEmptyKey_ThrowsArgumentException(CancellationToken cancellationToken)
    {
        var store = new RedisIdempotencyStore(
            Mock.Of<IConnectionMultiplexer>().Object,
            Options.Create(new RedisIdempotencyKeyOptions())
        );

        _ = await Assert
            .That(async () => await store.StoreAsync(string.Empty, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ExistsAsync_WithNonExistentKey_ReturnsFalse(CancellationToken cancellationToken)
    {
        var databaseMock = Mock.Of<IDatabase>();
        _ = databaseMock.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(false);

        var multiplexerMock = Mock.Of<IConnectionMultiplexer>();
        _ = multiplexerMock.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(databaseMock.Object);

        var store = new RedisIdempotencyStore(multiplexerMock.Object, Options.Create(new RedisIdempotencyKeyOptions()));

        var result = await store.ExistsAsync("test-key", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ExistsAsync_WithExistingKey_ReturnsTrue(CancellationToken cancellationToken)
    {
        var databaseMock = Mock.Of<IDatabase>();
        _ = databaseMock.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);

        var multiplexerMock = Mock.Of<IConnectionMultiplexer>();
        _ = multiplexerMock.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(databaseMock.Object);

        var store = new RedisIdempotencyStore(multiplexerMock.Object, Options.Create(new RedisIdempotencyKeyOptions()));

        var result = await store.ExistsAsync("test-key", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task StoreAsync_WhenKeyNotExists_Succeeds(CancellationToken cancellationToken)
    {
        var databaseMock = Mock.Of<IDatabase>();
        _ = databaseMock
            .StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>())
            .Returns(true);

        var multiplexerMock = Mock.Of<IConnectionMultiplexer>();
        _ = multiplexerMock.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(databaseMock.Object);

        var store = new RedisIdempotencyStore(multiplexerMock.Object, Options.Create(new RedisIdempotencyKeyOptions()));

        await store.StoreAsync("test-key", cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task StoreAsync_WhenKeyAlreadyExists_DoesNotThrow(CancellationToken cancellationToken)
    {
        var databaseMock = Mock.Of<IDatabase>();

        // StringSetAsync with When.NotExists returns false when the key already existed
        _ = databaseMock
            .StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>())
            .Returns(false);

        var multiplexerMock = Mock.Of<IConnectionMultiplexer>();
        _ = multiplexerMock.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(databaseMock.Object);

        var store = new RedisIdempotencyStore(multiplexerMock.Object, Options.Create(new RedisIdempotencyKeyOptions()));

        // Must not throw even when key already existed (StringSetAsync returned false)
        await store.StoreAsync("test-key", cancellationToken).ConfigureAwait(false);
    }
}
