namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Interceptors")]
public sealed class EventFilterInterceptorTests
{
    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var interceptor = new EventFilterInterceptor<TestEvent>([]);
        var testEvent = new TestEvent();

        _ = await Assert
            .That(() => interceptor.HandleAsync(testEvent, null!, cancellationToken))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoFilters_InvokesHandler(CancellationToken cancellationToken)
    {
        var interceptor = new EventFilterInterceptor<TestEvent>([]);
        var testEvent = new TestEvent();
        var handlerCalled = false;

        await interceptor
            .HandleAsync(
                testEvent,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(handlerCalled).IsTrue();
    }

    [Test]
    public async Task HandleAsync_AllFiltersReturnTrue_InvokesHandler(CancellationToken cancellationToken)
    {
        var filters = new List<IEventFilter<TestEvent>> { new AlwaysTrueFilter(), new AlwaysTrueFilter() };
        var interceptor = new EventFilterInterceptor<TestEvent>(filters);
        var testEvent = new TestEvent();
        var handlerCalled = false;

        await interceptor
            .HandleAsync(
                testEvent,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(handlerCalled).IsTrue();
    }

    [Test]
    public async Task HandleAsync_OneFilterReturnsFalse_SkipsHandler(CancellationToken cancellationToken)
    {
        var filters = new List<IEventFilter<TestEvent>> { new AlwaysTrueFilter(), new AlwaysFalseFilter() };
        var interceptor = new EventFilterInterceptor<TestEvent>(filters);
        var testEvent = new TestEvent();
        var handlerCalled = false;

        await interceptor
            .HandleAsync(
                testEvent,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(handlerCalled).IsFalse();
    }

    [Test]
    public async Task HandleAsync_FirstFilterReturnsFalse_DoesNotEvaluateRemainingFilters(
        CancellationToken cancellationToken
    )
    {
        var secondFilterEvaluated = false;
        var filters = new List<IEventFilter<TestEvent>>
        {
            new AlwaysFalseFilter(),
            new CallbackFilter(
                (_, _) =>
                {
                    secondFilterEvaluated = true;
                    return ValueTask.FromResult(true);
                }
            ),
        };
        var interceptor = new EventFilterInterceptor<TestEvent>(filters);
        var testEvent = new TestEvent();

        await interceptor.HandleAsync(testEvent, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(secondFilterEvaluated).IsFalse();
    }

    [Test]
    public async Task HandleAsync_SingleTrueFilter_InvokesHandlerWithSameEvent(CancellationToken cancellationToken)
    {
        var filters = new List<IEventFilter<TestEvent>> { new AlwaysTrueFilter() };
        var interceptor = new EventFilterInterceptor<TestEvent>(filters);
        var testEvent = new TestEvent();
        TestEvent? receivedEvent = null;

        await interceptor
            .HandleAsync(
                testEvent,
                (evt, _) =>
                {
                    receivedEvent = evt;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(receivedEvent).IsSameReferenceAs(testEvent);
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class AlwaysTrueFilter : IEventFilter<TestEvent>
    {
        public ValueTask<bool> ShouldHandleAsync(TestEvent message, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class AlwaysFalseFilter : IEventFilter<TestEvent>
    {
        public ValueTask<bool> ShouldHandleAsync(TestEvent message, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class CallbackFilter(Func<TestEvent, CancellationToken, ValueTask<bool>> callback)
        : IEventFilter<TestEvent>
    {
        public ValueTask<bool> ShouldHandleAsync(TestEvent message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }
}
