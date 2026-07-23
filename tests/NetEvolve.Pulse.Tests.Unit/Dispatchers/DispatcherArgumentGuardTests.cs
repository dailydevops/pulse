namespace NetEvolve.Pulse.Tests.Unit.Dispatchers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Pinning tests for argument-null guards on <see cref="IEventDispatcher"/> implementations
/// that were previously missing or inconsistent (DEEP-G-03).
/// <para>
/// <see cref="SequentialEventDispatcher"/> and the <c>invoker</c> guard on
/// <see cref="PrioritizedEventDispatcher"/> are already covered by <c>DispatcherInvariantTests</c>;
/// this file pins the remaining permutations so all four dispatchers fail fast with
/// <see cref="ArgumentNullException"/> for <c>handlers</c> and <c>invoker</c>.
/// </para>
/// </summary>
[TestGroup("Dispatchers")]
public sealed class DispatcherArgumentGuardTests
{
    [Test]
    public async Task ParallelEventDispatcher_NullHandlers_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new ParallelEventDispatcher();
        var msg = new TestEvent();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await dispatcher
                .DispatchAsync<TestEvent>(msg, null!, (h, m, ct) => h.HandleAsync(m, ct), cancellationToken)
                .ConfigureAwait(false)
        );

        _ = await Assert.That(ex!.ParamName).IsEqualTo("handlers");
    }

    [Test]
    public async Task ParallelEventDispatcher_NullInvoker_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new ParallelEventDispatcher();
        var msg = new TestEvent();
        var handlers = new List<IEventHandler<TestEvent>>();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await dispatcher.DispatchAsync(msg, handlers, null!, cancellationToken).ConfigureAwait(false)
        );

        _ = await Assert.That(ex!.ParamName).IsEqualTo("invoker");
    }

    [Test]
    public async Task RateLimitedEventDispatcher_NullHandlers_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new RateLimitedEventDispatcher();
        var msg = new TestEvent();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await dispatcher
                .DispatchAsync<TestEvent>(msg, null!, (h, m, ct) => h.HandleAsync(m, ct), cancellationToken)
                .ConfigureAwait(false)
        );

        _ = await Assert.That(ex!.ParamName).IsEqualTo("handlers");
    }

    [Test]
    public async Task RateLimitedEventDispatcher_NullInvoker_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new RateLimitedEventDispatcher();
        var msg = new TestEvent();
        var handlers = new List<IEventHandler<TestEvent>>();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await dispatcher.DispatchAsync(msg, handlers, null!, cancellationToken).ConfigureAwait(false)
        );

        _ = await Assert.That(ex!.ParamName).IsEqualTo("invoker");
    }

    [Test]
    public async Task PrioritizedEventDispatcher_NullHandlers_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new PrioritizedEventDispatcher();
        var msg = new TestEvent();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await dispatcher
                .DispatchAsync<TestEvent>(msg, null!, (h, m, ct) => h.HandleAsync(m, ct), cancellationToken)
                .ConfigureAwait(false)
        );

        _ = await Assert.That(ex!.ParamName).IsEqualTo("handlers");
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
