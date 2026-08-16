namespace NetEvolve.Pulse.Tests.Unit.Internals;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

[TestGroup("Internals")]
public class InMemoryCacheKeyRegistryTests
{
    private static readonly string[] ExpectedKeys = ["key-1", "key-2", "key-3"];

    [Test]
    public async Task Register_Then_GetKeysForType_ReturnsRegisteredKey()
    {
        var registry = new InMemoryCacheKeyRegistry();

        registry.Register(typeof(SampleQueryA), "key-1");

        var keys = registry.GetKeysForType(typeof(SampleQueryA));

        _ = await Assert.That(keys).IsEquivalentTo(["key-1"]);
    }

    [Test]
    public async Task Register_MultipleCallsForSameType_AccumulatesAllKeys()
    {
        var registry = new InMemoryCacheKeyRegistry();

        registry.Register(typeof(SampleQueryA), "key-1");
        registry.Register(typeof(SampleQueryA), "key-2");
        registry.Register(typeof(SampleQueryA), "key-3");

        var keys = registry.GetKeysForType(typeof(SampleQueryA));

        _ = await Assert
            .That(keys.OrderBy(x => x, StringComparer.Ordinal))
            .IsEquivalentTo(ExpectedKeys.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Test]
    public async Task GetKeysForType_WithNoRegistrations_ReturnsEmptyReadOnlyList()
    {
        var registry = new InMemoryCacheKeyRegistry();

        var keys = registry.GetKeysForType(typeof(SampleQueryA));

        using (Assert.Multiple())
        {
            _ = await Assert.That(keys).IsNotNull();
            _ = await Assert.That(keys).IsEmpty();
            _ = await Assert.That(keys).IsAssignableTo<IReadOnlyList<string>>();
        }
    }

    [Test]
    public async Task RemoveType_ClearsKeysForType_WhileLeavingOtherTypesUntouched()
    {
        var registry = new InMemoryCacheKeyRegistry();
        registry.Register(typeof(SampleQueryA), "key-1");
        registry.Register(typeof(SampleQueryA), "key-2");
        registry.Register(typeof(SampleQueryB), "other-key");

        registry.RemoveType(typeof(SampleQueryA));

        using (Assert.Multiple())
        {
            _ = await Assert.That(registry.GetKeysForType(typeof(SampleQueryA))).IsEmpty();
            _ = await Assert.That(registry.GetKeysForType(typeof(SampleQueryB))).IsEquivalentTo(["other-key"]);
        }
    }

    [Test]
    public async Task RemoveType_WithNoRegistrations_DoesNotThrow()
    {
        var registry = new InMemoryCacheKeyRegistry();

        _ = await Assert.That(() => registry.RemoveType(typeof(SampleQueryA))).ThrowsNothing();
    }

    [Test]
    public async Task Register_ConcurrentCallsAcrossMultipleTypes_AllKeysRetrievableWithNoLostWrites(
        CancellationToken cancellationToken = default
    )
    {
        var registry = new InMemoryCacheKeyRegistry();
        var types = new[] { typeof(SampleQueryA), typeof(SampleQueryB), typeof(SampleQueryC) };
        const int keysPerType = 200;

        var tasks = new List<Task>();
        foreach (var type in types)
        {
            for (var i = 0; i < keysPerType; i++)
            {
                var capturedType = type;
                var capturedKey = $"{type.Name}-{i}";
                tasks.Add(
                    Task.Run(() => registry.Register(capturedType, capturedKey), cancellationToken: cancellationToken)
                );
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            foreach (var type in types)
            {
                var expected = Enumerable
                    .Range(0, keysPerType)
                    .Select(i => $"{type.Name}-{i}")
                    .OrderBy(x => x, StringComparer.Ordinal);
                var actual = registry.GetKeysForType(type).OrderBy(x => x, StringComparer.Ordinal);

                _ = await Assert.That(actual).IsEquivalentTo(expected);
            }
        }
    }

    [Test]
    public async Task Register_When_queryType_is_null_throws_ArgumentNullException()
    {
        var registry = new InMemoryCacheKeyRegistry();

        _ = await Assert.That(() => registry.Register(null!, "key")).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Register_When_cacheKey_is_null_throws_ArgumentException()
    {
        var registry = new InMemoryCacheKeyRegistry();

        _ = await Assert.That(() => registry.Register(typeof(SampleQueryA), null!)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Register_When_cacheKey_is_empty_throws_ArgumentException()
    {
        var registry = new InMemoryCacheKeyRegistry();

        _ = await Assert.That(() => registry.Register(typeof(SampleQueryA), string.Empty)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Register_When_cacheKey_is_whitespace_throws_ArgumentException()
    {
        var registry = new InMemoryCacheKeyRegistry();

        _ = await Assert.That(() => registry.Register(typeof(SampleQueryA), "   ")).Throws<ArgumentException>();
    }

    [Test]
    public async Task GetKeysForType_When_queryType_is_null_throws_ArgumentNullException()
    {
        var registry = new InMemoryCacheKeyRegistry();

        _ = await Assert.That(() => registry.GetKeysForType(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task RemoveType_When_queryType_is_null_throws_ArgumentNullException()
    {
        var registry = new InMemoryCacheKeyRegistry();

        _ = await Assert.That(() => registry.RemoveType(null!)).Throws<ArgumentNullException>();
    }

    // ── Private test types ───────────────────────────────────────────────────

#pragma warning disable S2094 // Classes intentionally empty; used only as distinct Type markers via typeof()
    private sealed class SampleQueryA;

    private sealed class SampleQueryB;

    private sealed class SampleQueryC;
#pragma warning restore S2094
}
