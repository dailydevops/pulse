namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="PredicateEventFilter{TEvent}"/> — the inline-predicate wrapper used by
/// <c>AddEventFilter(this IMediatorBuilder, Func&lt;TEvent, CancellationToken, ValueTask&lt;bool&gt;&gt;)</c>.
/// Pins the null-predicate constructor invariant and verifies pass-through delegation to the predicate.
/// </summary>
[TestGroup("Interceptors")]
public sealed class PredicateEventFilterTests
{
    [Test]
    public async Task Constructor_NullPredicate_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>("predicate", () => _ = new PredicateEventFilter<TestEvent>(null!));

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [Test]
    public async Task ShouldHandleAsync_DelegatesToPredicate(CancellationToken cancellationToken)
    {
        var filter = new PredicateEventFilter<TestEvent>((_, _) => ValueTask.FromResult(true));

        var result = await filter.ShouldHandleAsync(new TestEvent(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ShouldHandleAsync_PropagatesFalseFromPredicate(CancellationToken cancellationToken)
    {
        var filter = new PredicateEventFilter<TestEvent>((_, _) => ValueTask.FromResult(false));

        var result = await filter.ShouldHandleAsync(new TestEvent(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ShouldHandleAsync_PassesEventAndTokenToPredicate(CancellationToken cancellationToken)
    {
        TestEvent? capturedEvent = null;
        var capturedToken = default(CancellationToken);
        var filter = new PredicateEventFilter<TestEvent>(
            (evt, ct) =>
            {
                capturedEvent = evt;
                capturedToken = ct;
                return ValueTask.FromResult(true);
            }
        );
        var testEvent = new TestEvent();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = await filter.ShouldHandleAsync(testEvent, cts.Token).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedEvent).IsSameReferenceAs(testEvent);
            _ = await Assert.That(capturedToken).IsEqualTo(cts.Token);
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
