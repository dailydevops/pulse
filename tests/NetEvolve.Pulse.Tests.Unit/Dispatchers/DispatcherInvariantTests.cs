namespace NetEvolve.Pulse.Tests.Unit.Dispatchers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Cross-cutting invariant tests for the four event dispatchers:
/// <see cref="ParallelEventDispatcher"/>, <see cref="SequentialEventDispatcher"/>,
/// <see cref="PrioritizedEventDispatcher"/>, <see cref="RateLimitedEventDispatcher"/>.
///
/// Focus: handler failure aggregation semantics — per the XML docs, every dispatcher in this slice
/// must execute every handler regardless of individual failures and surface the resulting exceptions
/// as a single <see cref="AggregateException"/>.
/// </summary>
[TestGroup("Dispatchers")]
public sealed class DispatcherInvariantTests
{
    private static readonly int[] s_expectedSequentialOrder = [1, 2, 3];

    // ----- ParallelEventDispatcher -----

    [Test]
    public async Task ParallelEventDispatcher_OneHandlerThrows_OtherHandlersStillExecuteAndAggregateThrown(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new ParallelEventDispatcher();
        var msg = new TestEvent();
        var executedIds = new ConcurrentBag<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new ConcurrentRecordingHandler(1, executedIds),
            new ConcurrentThrowingHandler(2, executedIds, () => new InvalidOperationException("boom")),
            new ConcurrentRecordingHandler(3, executedIds),
        };

        var ex = await Assert.ThrowsAsync<AggregateException>(async () =>
            await dispatcher
                .DispatchAsync(msg, handlers, (h, m, ct) => h.HandleAsync(m, ct), cancellationToken)
                .ConfigureAwait(false)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(executedIds.ToArray()).Contains(1);
            _ = await Assert.That(executedIds.ToArray()).Contains(2);
            _ = await Assert.That(executedIds.ToArray()).Contains(3);
            _ = await Assert.That(ex!.InnerExceptions).Count().IsEqualTo(1);
            _ = await Assert.That(ex.InnerExceptions[0]).IsTypeOf<InvalidOperationException>();
        }
    }

    [Test]
    public async Task ParallelEventDispatcher_MultipleHandlersThrow_AggregateContainsAllExceptions(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new ParallelEventDispatcher();
        var msg = new TestEvent();
        var executedIds = new ConcurrentBag<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new ConcurrentThrowingHandler(1, executedIds, () => new InvalidOperationException("first")),
            new ConcurrentThrowingHandler(2, executedIds, () => new ArgumentException("second")),
            new ConcurrentRecordingHandler(3, executedIds),
        };

        var ex = await Assert.ThrowsAsync<AggregateException>(async () =>
            await dispatcher
                .DispatchAsync(msg, handlers, (h, m, ct) => h.HandleAsync(m, ct), cancellationToken)
                .ConfigureAwait(false)
        );

        _ = await Assert.That(ex!.InnerExceptions).Count().IsEqualTo(2);
    }

    // ----- SequentialEventDispatcher -----

    [Test]
    public async Task SequentialEventDispatcher_NullHandlers_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new SequentialEventDispatcher();
        var msg = new TestEvent();

        _ = await Assert
            .That(async () =>
                await dispatcher
                    .DispatchAsync<TestEvent>(msg, null!, (h, m, ct) => h.HandleAsync(m, ct), cancellationToken)
                    .ConfigureAwait(false)
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task SequentialEventDispatcher_NullInvoker_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new SequentialEventDispatcher();
        var msg = new TestEvent();
        var handlers = new List<IEventHandler<TestEvent>>();

        _ = await Assert
            .That(async () =>
                await dispatcher.DispatchAsync(msg, handlers, null!, cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task SequentialEventDispatcher_OneHandlerThrows_SubsequentHandlersStillRunAndAggregateThrown(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new SequentialEventDispatcher();
        var msg = new TestEvent();
        var executedIds = new List<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new ListRecordingHandler(1, executedIds),
            new ListThrowingHandler(2, executedIds, () => new InvalidOperationException("boom")),
            new ListRecordingHandler(3, executedIds),
        };

        var ex = await Assert.ThrowsAsync<AggregateException>(async () =>
            await dispatcher
                .DispatchAsync(msg, handlers, (h, m, ct) => h.HandleAsync(m, ct), cancellationToken)
                .ConfigureAwait(false)
        );

        using (Assert.Multiple())
        {
            // All three must have recorded execution
            _ = await Assert.That(executedIds).IsEquivalentTo(s_expectedSequentialOrder);
            _ = await Assert.That(ex!.InnerExceptions).Count().IsEqualTo(1);
            _ = await Assert.That(ex.InnerExceptions[0]).IsTypeOf<InvalidOperationException>();
        }
    }

    // ----- PrioritizedEventDispatcher -----

    [Test]
    public async Task PrioritizedEventDispatcher_NullInvoker_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new PrioritizedEventDispatcher();
        var msg = new TestEvent();
        var handlers = new List<IEventHandler<TestEvent>>();

        _ = await Assert
            .That(async () =>
                await dispatcher.DispatchAsync(msg, handlers, null!, cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task PrioritizedEventDispatcher_OneHandlerThrows_SubsequentPriorityGroupsStillRun(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new PrioritizedEventDispatcher();
        var msg = new TestEvent();
        var executedIds = new ConcurrentBag<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new ThrowingPrioritizedHandler(1, 0, executedIds, () => new InvalidOperationException("boom")),
            new RecordingPrioritizedHandler(2, 100, executedIds),
            new RecordingPrioritizedHandler(3, 200, executedIds),
        };

        var ex = await Assert.ThrowsAsync<AggregateException>(async () =>
            await dispatcher
                .DispatchAsync(msg, handlers, (h, m, ct) => h.HandleAsync(m, ct), cancellationToken)
                .ConfigureAwait(false)
        );

        using (Assert.Multiple())
        {
            // The failure in the first priority group must NOT prevent subsequent groups.
            _ = await Assert.That(executedIds.ToArray()).Contains(1);
            _ = await Assert.That(executedIds.ToArray()).Contains(2);
            _ = await Assert.That(executedIds.ToArray()).Contains(3);
            _ = await Assert.That(ex!.InnerExceptions).Count().IsEqualTo(1);
            _ = await Assert.That(ex.InnerExceptions[0]).IsTypeOf<InvalidOperationException>();
        }
    }

    // ----- RateLimitedEventDispatcher -----

    [Test]
    public async Task RateLimitedEventDispatcher_OneHandlerThrows_OtherHandlersStillExecuteAndAggregateThrown(
        CancellationToken cancellationToken
    )
    {
        var dispatcher = new RateLimitedEventDispatcher(maxConcurrency: 2);
        var msg = new TestEvent();
        var executedIds = new ConcurrentBag<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new ConcurrentRecordingHandler(1, executedIds),
            new ConcurrentThrowingHandler(2, executedIds, () => new InvalidOperationException("boom")),
            new ConcurrentRecordingHandler(3, executedIds),
        };

        var ex = await Assert.ThrowsAsync<AggregateException>(async () =>
            await dispatcher
                .DispatchAsync(msg, handlers, (h, m, ct) => h.HandleAsync(m, ct), cancellationToken)
                .ConfigureAwait(false)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(executedIds.ToArray()).Contains(1);
            _ = await Assert.That(executedIds.ToArray()).Contains(2);
            _ = await Assert.That(executedIds.ToArray()).Contains(3);
            _ = await Assert.That(ex!.InnerExceptions).Count().IsEqualTo(1);
        }
    }

    // ----- Test fixtures -----

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class ConcurrentRecordingHandler : IEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly ConcurrentBag<int> _executedIds;

        public ConcurrentRecordingHandler(int id, ConcurrentBag<int> executedIds)
        {
            _id = id;
            _executedIds = executedIds;
        }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executedIds.Add(_id);
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentThrowingHandler : IEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly ConcurrentBag<int> _executedIds;
        private readonly Func<Exception> _exceptionFactory;

        public ConcurrentThrowingHandler(int id, ConcurrentBag<int> executedIds, Func<Exception> exceptionFactory)
        {
            _id = id;
            _executedIds = executedIds;
            _exceptionFactory = exceptionFactory;
        }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executedIds.Add(_id);
            throw _exceptionFactory();
        }
    }

    private sealed class ListRecordingHandler : IEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly List<int> _executedIds;

        public ListRecordingHandler(int id, List<int> executedIds)
        {
            _id = id;
            _executedIds = executedIds;
        }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executedIds.Add(_id);
            return Task.CompletedTask;
        }
    }

    private sealed class ListThrowingHandler : IEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly List<int> _executedIds;
        private readonly Func<Exception> _exceptionFactory;

        public ListThrowingHandler(int id, List<int> executedIds, Func<Exception> exceptionFactory)
        {
            _id = id;
            _executedIds = executedIds;
            _exceptionFactory = exceptionFactory;
        }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executedIds.Add(_id);
            throw _exceptionFactory();
        }
    }

    private sealed class RecordingPrioritizedHandler : IPrioritizedEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly ConcurrentBag<int> _executedIds;

        public RecordingPrioritizedHandler(int id, int priority, ConcurrentBag<int> executedIds)
        {
            _id = id;
            Priority = priority;
            _executedIds = executedIds;
        }

        public int Priority { get; }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executedIds.Add(_id);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPrioritizedHandler : IPrioritizedEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly ConcurrentBag<int> _executedIds;
        private readonly Func<Exception> _exceptionFactory;

        public ThrowingPrioritizedHandler(
            int id,
            int priority,
            ConcurrentBag<int> executedIds,
            Func<Exception> exceptionFactory
        )
        {
            _id = id;
            Priority = priority;
            _executedIds = executedIds;
            _exceptionFactory = exceptionFactory;
        }

        public int Priority { get; }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executedIds.Add(_id);
            throw _exceptionFactory();
        }
    }
}
