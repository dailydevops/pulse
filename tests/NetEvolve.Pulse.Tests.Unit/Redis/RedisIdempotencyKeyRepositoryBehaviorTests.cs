namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using StackExchange.Redis;
using TUnit.Core;

/// <summary>
/// Behavioral invariants for <see cref="RedisIdempotencyKeyRepository"/>.
/// IConnectionMultiplexer/IDatabase are very large interfaces; this file uses
/// <see cref="DispatchProxy"/> to intercept the few methods the repository actually calls
/// (<c>GetDatabase</c>, <c>StringSetAsync</c>, <c>StringGetAsync</c>) and capture their
/// arguments. Every other call routes to <see cref="NotImplementedException"/> so accidental
/// new dependencies on Redis methods surface immediately.
/// </summary>
[TestGroup("Redis")]
public sealed class RedisIdempotencyKeyRepositoryBehaviorTests
{
#pragma warning disable CA1034 // Test-only nested helper types; not part of any public API.
#pragma warning disable CA1002 // List<T> as accumulator is fine for test fixtures.

    internal sealed record StringSetCall(RedisKey Key, RedisValue Value, TimeSpan? Expiry, When When);

    internal class FakeDatabase : DispatchProxy
    {
        public List<StringSetCall> StringSetCalls { get; } = new();
        public Dictionary<string, RedisValue> Storage { get; } = new(StringComparer.Ordinal);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new InvalidOperationException("Null target method");
            }

            switch (targetMethod.Name)
            {
                case nameof(IDatabase.StringSetAsync):
                {
                    // The repo calls the overload (RedisKey, RedisValue, TimeSpan?, When, CommandFlags)
                    if (args is { Length: >= 4 } && args[0] is RedisKey key && args[1] is RedisValue value)
                    {
                        var expiry = (TimeSpan?)args[2];
                        var when = (When)(args[3] ?? When.Always);
                        StringSetCalls.Add(new StringSetCall(key, value, expiry, when));
#pragma warning disable S8969 // RedisKey's implicit string conversion is annotated nullable; the value is never null here
                        var keyStr = (string)key!;
#pragma warning restore S8969
                        if (when == When.NotExists && Storage.ContainsKey(keyStr))
                        {
                            return Task.FromResult(false);
                        }
                        Storage[keyStr] = value;
                        return Task.FromResult(true);
                    }
                    throw new NotSupportedException($"Unexpected StringSetAsync overload: {targetMethod}");
                }
                case nameof(IDatabase.StringGetAsync):
                {
                    if (args is { Length: >= 1 } && args[0] is RedisKey key)
                    {
#pragma warning disable S8969 // RedisKey's implicit string conversion is annotated nullable; the value is never null here
                        return Task.FromResult(Storage.TryGetValue((string)key!, out var v) ? v : RedisValue.Null);
#pragma warning restore S8969
                    }
                    throw new NotSupportedException($"Unexpected StringGetAsync overload: {targetMethod}");
                }
                default:
                    throw new NotImplementedException($"FakeDatabase has no behavior for {targetMethod}");
            }
        }
    }

    internal class FakeMultiplexer : DispatchProxy
    {
        public IDatabase? Database { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new InvalidOperationException("Null target method");
            }

            if (targetMethod.Name == nameof(IConnectionMultiplexer.GetDatabase))
            {
                return Database!;
            }

            throw new NotImplementedException($"FakeMultiplexer has no behavior for {targetMethod}");
        }
    }

    private static (IConnectionMultiplexer Mux, FakeDatabase Capture) BuildFakes()
    {
        var dbProxy = DispatchProxy.Create<IDatabase, FakeDatabase>();
        var muxProxy = DispatchProxy.Create<IConnectionMultiplexer, FakeMultiplexer>();
        ((FakeMultiplexer)(object)muxProxy).Database = dbProxy;
        return (muxProxy, (FakeDatabase)(object)dbProxy);
    }

    // INVARIANT (Q07): StoreAsync MUST use When.NotExists so concurrent writers cannot both
    // succeed; the application interprets a successful Store as atomically claiming the key.
    [Test]
    public async Task StoreAsync_Uses_StringSet_with_When_NotExists(CancellationToken cancellationToken)
    {
        var (mux, capture) = BuildFakes();

        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        await repo.StoreAsync("k1", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(1);
        _ = await Assert.That(capture.StringSetCalls[0].When).IsEqualTo(When.NotExists);
    }

    // INVARIANT (Q07): Default physical TTL is 24h when no TTL is configured.
    [Test]
    public async Task StoreAsync_Default_TTL_is_24h(CancellationToken cancellationToken)
    {
        var (mux, capture) = BuildFakes();

        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        await repo.StoreAsync("k1", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(1);
        _ = await Assert.That(capture.StringSetCalls[0].Expiry).IsEqualTo(TimeSpan.FromHours(24));
    }

    // INVARIANT (Q07): With configured TTL, physical TTL = TTL + 1h headroom so the
    // TimeProvider-based logical TTL check fires before the physical eviction.
    [Test]
    public async Task StoreAsync_Configured_TTL_adds_one_hour_headroom(CancellationToken cancellationToken)
    {
        var (mux, capture) = BuildFakes();

        var options = Options.Create(new IdempotencyKeyOptions { TimeToLive = TimeSpan.FromHours(6) });
        var repo = new RedisIdempotencyKeyRepository(mux, options);

        await repo.StoreAsync("k1", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(1);
        _ = await Assert.That(capture.StringSetCalls[0].Expiry).IsEqualTo(TimeSpan.FromHours(7));
    }

    // INVARIANT (Q07): Key is namespaced "{Schema}:{TableName}:{key}" to prevent cross-tenant collisions.
    [Test]
    public async Task StoreAsync_Prefixes_key_with_schema_and_table(CancellationToken cancellationToken)
    {
        var (mux, capture) = BuildFakes();

        var options = Options.Create(new IdempotencyKeyOptions { Schema = "tenant1", TableName = "idem" });
        var repo = new RedisIdempotencyKeyRepository(mux, options);

        await repo.StoreAsync("ident-key", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(1);
#pragma warning disable S8969 // RedisKey's implicit string conversion is annotated nullable; the value is never null here
        _ = await Assert.That((string)capture.StringSetCalls[0].Key!).IsEqualTo("tenant1:idem:ident-key");
#pragma warning restore S8969
    }

    // INVARIANT (Q07): Stored value is the ISO-8601 round-trip ("O") timestamp - what
    // ExistsAsync(validFrom) parses back to enforce the logical TTL.
    [Test]
    public async Task StoreAsync_Persists_timestamp_in_ISO8601_roundtrip_format(CancellationToken cancellationToken)
    {
        var (mux, capture) = BuildFakes();
        var createdAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        await repo.StoreAsync("k1", createdAt, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(1);
#pragma warning disable S8969 // RedisValue's implicit string conversion is annotated nullable; the value is never null here
        _ = await Assert.That((string)capture.StringSetCalls[0].Value!).IsEqualTo("2025-01-01T10:00:00.0000000+00:00");
#pragma warning restore S8969
    }

    [Test]
    public async Task ExistsAsync_With_no_validFrom_Returns_true_when_value_present(CancellationToken cancellationToken)
    {
        var (mux, _) = BuildFakes();
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        await repo.StoreAsync("k1", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(await repo.ExistsAsync("k1", null, cancellationToken).ConfigureAwait(false)).IsTrue();
    }

    [Test]
    public async Task ExistsAsync_With_no_validFrom_Returns_false_when_value_absent(CancellationToken cancellationToken)
    {
        var (mux, _) = BuildFakes();
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        _ = await Assert.That(await repo.ExistsAsync("k1", null, cancellationToken).ConfigureAwait(false)).IsFalse();
    }

    // INVARIANT (Q07): TTL window: stored value whose creation predates validFrom is treated
    // as expired/absent.
    [Test]
    public async Task ExistsAsync_With_validFrom_after_creation_returns_false(CancellationToken cancellationToken)
    {
        var (mux, _) = BuildFakes();
        var createdAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        await repo.StoreAsync("k1", createdAt, cancellationToken).ConfigureAwait(false);

        var validFrom = createdAt.AddHours(1);

        _ = await Assert
            .That(await repo.ExistsAsync("k1", validFrom, cancellationToken).ConfigureAwait(false))
            .IsFalse();
    }

    [Test]
    public async Task ExistsAsync_With_validFrom_before_creation_returns_true(CancellationToken cancellationToken)
    {
        var (mux, _) = BuildFakes();
        var createdAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        await repo.StoreAsync("k1", createdAt, cancellationToken).ConfigureAwait(false);

        var validFrom = createdAt.AddHours(-1);

        _ = await Assert
            .That(await repo.ExistsAsync("k1", validFrom, cancellationToken).ConfigureAwait(false))
            .IsTrue();
    }

    // INVARIANT (Q07): A second Store with the SAME key MUST NOT overwrite the original
    // timestamp - When.NotExists guarantees that.
    [Test]
    public async Task StoreAsync_Second_Store_does_not_overwrite_original_value(CancellationToken cancellationToken)
    {
        var (mux, capture) = BuildFakes();
        var first = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var second = first.AddHours(5);
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        await repo.StoreAsync("k1", first, cancellationToken).ConfigureAwait(false);
        await repo.StoreAsync("k1", second, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(2);
#pragma warning disable S8969 // RedisKey/RedisValue's implicit string conversion is annotated nullable; the value is never null here
        var stored = capture.Storage[(string)capture.StringSetCalls[0].Key!];
        _ = await Assert.That((string)stored!).IsEqualTo("2025-01-01T10:00:00.0000000+00:00");
#pragma warning restore S8969
    }
}
