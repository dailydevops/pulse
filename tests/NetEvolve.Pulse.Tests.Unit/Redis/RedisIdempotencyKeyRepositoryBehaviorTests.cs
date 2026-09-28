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
/// (<c>GetDatabase</c>, <c>StringSetAsync</c>, <c>StringGetAsync</c>, <c>CreateTransaction</c>) and capture their
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
        public List<RedisKey> StringGetCalls { get; } = new();
        public Dictionary<string, RedisValue> Storage { get; } = new(StringComparer.Ordinal);
        public List<FakeTransaction> Transactions { get; } = new();

        // Simulates a concurrent writer that changes the key before EXEC, so the compare-and-set fails.
        public bool TransactionConditionFails { get; set; }

        // Simulates the key expiring physically between SET NX and GET.
        public bool RemoveKeyOnGet { get; set; }

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
                        StringGetCalls.Add(key);
#pragma warning disable S8969 // RedisKey's implicit string conversion is annotated nullable; the value is never null here
                        if (RemoveKeyOnGet)
                        {
                            _ = Storage.Remove((string)key!);
                        }

                        return Task.FromResult(Storage.TryGetValue((string)key!, out var v) ? v : RedisValue.Null);
#pragma warning restore S8969
                    }
                    throw new NotSupportedException($"Unexpected StringGetAsync overload: {targetMethod}");
                }
                case nameof(IDatabase.CreateTransaction):
                {
                    var transactionProxy = DispatchProxy.Create<ITransaction, FakeTransaction>();
                    var transaction = (FakeTransaction)(object)transactionProxy;
                    transaction.Database = this;
                    Transactions.Add(transaction);
                    return transactionProxy;
                }
                default:
                    throw new NotImplementedException($"FakeDatabase has no behavior for {targetMethod}");
            }
        }
    }

    internal class FakeTransaction : DispatchProxy
    {
        public FakeDatabase? Database { get; set; }
        public List<Condition> Conditions { get; } = new();
        public List<StringSetCall> StringSetCalls { get; } = new();
        public bool? Executed { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new InvalidOperationException("Null target method");
            }

            switch (targetMethod.Name)
            {
                case nameof(ITransaction.AddCondition) when args is [Condition condition]:
                    Conditions.Add(condition);
                    return null;
                case nameof(ITransaction.StringSetAsync)
                    when args is { Length: >= 4 } && args[0] is RedisKey key && args[1] is RedisValue value:
                    StringSetCalls.Add(
                        new StringSetCall(key, value, (TimeSpan?)args[2], (When)(args[3] ?? When.Always))
                    );
                    return Task.FromResult(true);
                case nameof(ITransaction.ExecuteAsync):
                    Executed = !Database!.TransactionConditionFails;
                    if (Executed.Value)
                    {
                        foreach (var call in StringSetCalls)
                        {
#pragma warning disable S8969 // RedisKey's implicit string conversion is annotated nullable; the value is never null here
                            Database.Storage[(string)call.Key!] = call.Value;
#pragma warning restore S8969
                        }
                    }

                    return Task.FromResult(Executed.Value);
                default:
                    throw new NotImplementedException($"FakeTransaction has no behavior for {targetMethod}");
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
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();

        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        await repo.StoreAsync("k1", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(1);
        _ = await Assert.That(capture.StringSetCalls[0].When).IsEqualTo(When.NotExists);
    }

    // INVARIANT (#790): A null TimeToLive means "keys never expire", so no physical expiry is set.
    [Test]
    public async Task StoreAsync_Null_TTL_stores_key_without_expiry(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();

        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        await repo.StoreAsync("k1", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(1);
        _ = await Assert.That(capture.StringSetCalls[0].Expiry).IsNull();
    }

    // INVARIANT (Q07): With configured TTL, physical TTL = TTL + 1h headroom so the
    // TimeProvider-based logical TTL check fires before the physical eviction.
    [Test]
    public async Task StoreAsync_Configured_TTL_adds_one_hour_headroom(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, _) = BuildFakes();
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        await repo.StoreAsync("k1", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(await repo.ExistsAsync("k1", null, cancellationToken).ConfigureAwait(false)).IsTrue();
    }

    [Test]
    public async Task ExistsAsync_With_no_validFrom_Returns_false_when_value_absent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, _) = BuildFakes();
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        _ = await Assert.That(await repo.ExistsAsync("k1", null, cancellationToken).ConfigureAwait(false)).IsFalse();
    }

    // INVARIANT (Q07): TTL window: stored value whose creation predates validFrom is treated
    // as expired/absent.
    [Test]
    public async Task ExistsAsync_With_validFrom_after_creation_returns_false(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();

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

    // INVARIANT: an entry that cannot be parsed (e.g. legacy value) is treated as present, so the
    // store fails closed and never re-processes a request whose key exists.
    [Test]
    public async Task ExistsAsync_With_validFrom_and_unparsable_value_returns_true(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        capture.Storage["pulse:IdempotencyKey:k1"] = "not-a-timestamp";
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        _ = await Assert
            .That(await repo.ExistsAsync("k1", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false))
            .IsTrue();
    }

    // INVARIANT: the TTL window is inclusive - a key created exactly at validFrom is still present.
    [Test]
    public async Task ExistsAsync_With_validFrom_equal_to_creation_returns_true(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, _) = BuildFakes();
        var createdAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        await repo.StoreAsync("k1", createdAt, cancellationToken).ConfigureAwait(false);

        _ = await Assert
            .That(await repo.ExistsAsync("k1", createdAt, cancellationToken).ConfigureAwait(false))
            .IsTrue();
    }

    [Test]
    public async Task ExistsAsync_Reads_key_prefixed_with_schema_and_table(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        capture.Storage["tenant1:idem:k1"] = "2025-01-01T10:00:00.0000000+00:00";
        capture.Storage["k2"] = "2025-01-01T10:00:00.0000000+00:00";
        var options = Options.Create(new IdempotencyKeyOptions { Schema = "tenant1", TableName = "idem" });
        var repo = new RedisIdempotencyKeyRepository(mux, options);

        using (Assert.Multiple())
        {
            _ = await Assert.That(await repo.ExistsAsync("k1", null, cancellationToken).ConfigureAwait(false)).IsTrue();
            _ = await Assert
                .That(await repo.ExistsAsync("k2", null, cancellationToken).ConfigureAwait(false))
                .IsFalse();
        }
    }

    [Test]
    public async Task StoreAsync_Default_options_prefix_key_with_pulse_IdempotencyKey(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        await repo.StoreAsync("k1", DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(1);
#pragma warning disable S8969 // RedisKey's implicit string conversion is annotated nullable; the value is never null here
        _ = await Assert.That((string)capture.StringSetCalls[0].Key!).IsEqualTo("pulse:IdempotencyKey:k1");
#pragma warning restore S8969
    }

    [Test]
    public async Task ExistsAsync_With_cancelled_token_throws_without_calling_Redis()
    {
        var (mux, capture) = BuildFakes();
        capture.Storage["pulse:IdempotencyKey:k1"] = "2025-01-01T10:00:00.0000000+00:00";
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(() => repo.ExistsAsync("k1", null, cts.Token)).Throws<OperationCanceledException>();
            _ = await Assert.That(capture.StringGetCalls).IsEmpty();
        }
    }

    [Test]
    public async Task StoreAsync_With_cancelled_token_throws_without_calling_Redis()
    {
        var (mux, capture) = BuildFakes();
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(() => repo.StoreAsync("k1", DateTimeOffset.UtcNow, cts.Token))
                .Throws<OperationCanceledException>();
            _ = await Assert.That(capture.StringSetCalls).IsEmpty();
        }
    }

    // INVARIANT (#816): TryStoreAsync reserves an absent key with a single atomic SET NX including the physical TTL.
    [Test]
    public async Task TryStoreAsync_Absent_key_is_reserved_with_SET_NX(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        var options = Options.Create(new IdempotencyKeyOptions { TimeToLive = TimeSpan.FromHours(6) });
        var repo = new RedisIdempotencyKeyRepository(mux, options);
        var now = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var result = await repo.TryStoreAsync("k1", now, now.AddHours(-6), cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsTrue();
            _ = await Assert.That(capture.StringSetCalls).HasCount(1);
            _ = await Assert.That(capture.StringSetCalls[0].When).IsEqualTo(When.NotExists);
            _ = await Assert.That(capture.StringSetCalls[0].Expiry).IsEqualTo(TimeSpan.FromHours(7));
            _ = await Assert.That(capture.StringGetCalls).IsEmpty();
        }
    }

    // INVARIANT (#790, #816): without a TTL an existing key is never reserved again.
    [Test]
    public async Task TryStoreAsync_Existing_key_without_validFrom_returns_false(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        capture.Storage["pulse:IdempotencyKey:k1"] = "2000-01-01T00:00:00.0000000+00:00";
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));

        var result = await repo.TryStoreAsync("k1", DateTimeOffset.UtcNow, null, cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsFalse();
            _ = await Assert.That(capture.StringGetCalls).IsEmpty();
#pragma warning disable S8969 // RedisValue's implicit string conversion is annotated nullable; the value is never null here
            _ = await Assert
                .That((string)capture.Storage["pulse:IdempotencyKey:k1"]!)
                .IsEqualTo("2000-01-01T00:00:00.0000000+00:00");
#pragma warning restore S8969
        }
    }

    // INVARIANT (#816): a key created at or after validFrom is still valid and is not replaced.
    [Test]
    public async Task TryStoreAsync_Valid_existing_key_returns_false(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        capture.Storage["pulse:IdempotencyKey:k1"] = "2025-01-01T10:00:00.0000000+00:00";
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        var validFrom = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var result = await repo.TryStoreAsync("k1", validFrom.AddMinutes(30), validFrom, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsFalse();
    }

    // INVARIANT (#816): an unparseable value fails closed, like ExistsAsync, and is not replaced.
    [Test]
    public async Task TryStoreAsync_Unparsable_existing_value_returns_false(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        capture.Storage["pulse:IdempotencyKey:k1"] = "not-a-timestamp";
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        var now = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var result = await repo.TryStoreAsync("k1", now, now.AddHours(-1), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsFalse();
    }

    // INVARIANT (#816): a logically expired key is replaced only through a compare-and-set on the observed value.
    [Test]
    public async Task TryStoreAsync_Expired_existing_key_is_replaced_by_compare_and_set(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        capture.Storage["pulse:IdempotencyKey:k1"] = "2025-01-01T08:00:00.0000000+00:00";
        var options = Options.Create(new IdempotencyKeyOptions { TimeToLive = TimeSpan.FromHours(1) });
        var repo = new RedisIdempotencyKeyRepository(mux, options);
        var now = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var expectedCondition = Condition.StringEqual("pulse:IdempotencyKey:k1", "2025-01-01T08:00:00.0000000+00:00");

        var result = await repo.TryStoreAsync("k1", now, now.AddHours(-1), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.Transactions).HasCount(1);
        var transaction = capture.Transactions[0];

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsTrue();
            _ = await Assert.That(transaction.Conditions).HasCount(1);
            _ = await Assert.That(transaction.Conditions[0].ToString()).IsEqualTo(expectedCondition.ToString());
            _ = await Assert.That(transaction.StringSetCalls).HasCount(1);
            _ = await Assert.That(transaction.StringSetCalls[0].When).IsEqualTo(When.Always);
            _ = await Assert.That(transaction.StringSetCalls[0].Expiry).IsEqualTo(TimeSpan.FromHours(2));
            _ = await Assert.That(transaction.Executed).IsTrue();
#pragma warning disable S8969 // RedisValue's implicit string conversion is annotated nullable; the value is never null here
            _ = await Assert
                .That((string)capture.Storage["pulse:IdempotencyKey:k1"]!)
                .IsEqualTo("2025-01-01T10:00:00.0000000+00:00");
#pragma warning restore S8969
        }
    }

    // INVARIANT (#816): when a concurrent call changed the expired key first, the compare-and-set fails and
    // this call does not reserve the key.
    [Test]
    public async Task TryStoreAsync_Expired_key_changed_concurrently_returns_false(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        capture.Storage["pulse:IdempotencyKey:k1"] = "2025-01-01T08:00:00.0000000+00:00";
        capture.TransactionConditionFails = true;
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        var now = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var result = await repo.TryStoreAsync("k1", now, now.AddHours(-1), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.Transactions).HasCount(1);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsFalse();
            _ = await Assert.That(capture.Transactions[0].Executed).IsFalse();
#pragma warning disable S8969 // RedisValue's implicit string conversion is annotated nullable; the value is never null here
            _ = await Assert
                .That((string)capture.Storage["pulse:IdempotencyKey:k1"]!)
                .IsEqualTo("2025-01-01T08:00:00.0000000+00:00");
#pragma warning restore S8969
        }
    }

    // INVARIANT (#816): a key that expires physically between SET NX and GET is reserved with a second SET NX.
    [Test]
    public async Task TryStoreAsync_Key_expiring_between_SET_NX_and_GET_is_reserved_with_SET_NX(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, capture) = BuildFakes();
        capture.Storage["pulse:IdempotencyKey:k1"] = "2025-01-01T08:00:00.0000000+00:00";
        capture.RemoveKeyOnGet = true;
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        var now = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var result = await repo.TryStoreAsync("k1", now, now.AddHours(-1), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StringSetCalls).HasCount(2);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsTrue();
            _ = await Assert.That(capture.StringSetCalls[1].When).IsEqualTo(When.NotExists);
            _ = await Assert.That(capture.Transactions).IsEmpty();
        }
    }

    [Test]
    public async Task TryStoreAsync_With_cancelled_token_throws_without_calling_Redis()
    {
        var (mux, capture) = BuildFakes();
        var repo = new RedisIdempotencyKeyRepository(mux, Options.Create(new IdempotencyKeyOptions()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(() => repo.TryStoreAsync("k1", DateTimeOffset.UtcNow, null, cts.Token))
                .Throws<OperationCanceledException>();
            _ = await Assert.That(capture.StringSetCalls).IsEmpty();
        }
    }
}
