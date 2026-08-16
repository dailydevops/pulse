namespace NetEvolve.Pulse.Tests.Unit.Internals;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

[TestGroup("Internals")]
public class PulseMediatorTests
{
    [Test]
    public async Task Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        ILogger<PulseMediator>? logger = null;
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var timeProvider = TimeProvider.System;

        _ = Assert.Throws<ArgumentNullException>(
            "logger",
            () => _ = new PulseMediator(logger!, serviceProvider, timeProvider)
        );
    }

    [Test]
    public async Task Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        var logger = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider()
            .GetRequiredService<ILogger<PulseMediator>>();
        IServiceProvider? serviceProvider = null;
        var timeProvider = TimeProvider.System;

        _ = Assert.Throws<ArgumentNullException>(
            "serviceProvider",
            () => _ = new PulseMediator(logger, serviceProvider!, timeProvider)
        );
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var logger = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider()
            .GetRequiredService<ILogger<PulseMediator>>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        TimeProvider? timeProvider = null;

        _ = Assert.Throws<ArgumentNullException>(
            "timeProvider",
            () => _ = new PulseMediator(logger, serviceProvider, timeProvider!)
        );
    }

    [Test]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        var logger = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider()
            .GetRequiredService<ILogger<PulseMediator>>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var timeProvider = TimeProvider.System;

        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);

        using (Assert.Multiple())
        {
            _ = await Assert.That(mediator).IsNotNull();
            _ = await Assert.That(mediator).IsTypeOf<PulseMediator>();
        }
    }

    [Test]
    public async Task PublishAsync_WithNullMessage_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            "message",
            async () => await mediator.PublishAsync<TestEvent>(null!, cancellationToken).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task PublishAsync_WithNoHandlers_CompletesSuccessfully(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var testEvent = new TestEvent();

        await mediator.PublishAsync(testEvent, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(testEvent.PublishedAt).IsNotNull();
    }

    [Test]
    public async Task PublishAsync_WithHandlers_InvokesAllHandlers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler1 = new TestEventHandler();
        var handler2 = new TestEventHandler();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IEventHandler<TestEvent>>(handler1);
        _ = services.AddSingleton<IEventHandler<TestEvent>>(handler2);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var testEvent = new TestEvent();

        await mediator.PublishAsync(testEvent, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(handler1.HandledEvents).HasSingleItem();
            _ = await Assert.That(handler2.HandledEvents).HasSingleItem();
            _ = await Assert.That(handler1.HandledEvents[0]).IsSameReferenceAs(testEvent);
            _ = await Assert.That(handler2.HandledEvents[0]).IsSameReferenceAs(testEvent);
        }
    }

    [Test]
    public async Task PublishAsync_WithHandlerException_ContinuesExecutingOtherHandlersAndThrowsAggregate(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler1 = new ThrowingEventHandler();
        var handler2 = new TestEventHandler();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IEventHandler<TestEvent>>(handler1);
        _ = services.AddSingleton<IEventHandler<TestEvent>>(handler2);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var testEvent = new TestEvent();

        // Act & Assert - PublishAsync throws AggregateException containing the handler failure
        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            await mediator.PublishAsync(testEvent, cancellationToken).ConfigureAwait(false)
        );

        // Verify the exception contains the handler failure
        _ = await Assert.That(exception!.InnerExceptions).Count().IsEqualTo(1);
        _ = await Assert.That(exception.InnerExceptions[0]).IsAssignableTo<InvalidOperationException>();

        // Verify the successful handler still executed despite the failure
        _ = await Assert.That(handler2.HandledEvents).HasSingleItem();
    }

    [Test]
    public async Task PublishAsync_SetsPublishedAtTimestamp(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection().AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var testEvent = new TestEvent();
        var beforePublish = timeProvider.GetUtcNow();

        await mediator.PublishAsync(testEvent, cancellationToken).ConfigureAwait(false);

        var afterPublish = timeProvider.GetUtcNow();
        var publishedAt = testEvent.PublishedAt;
        using (Assert.Multiple())
        {
            _ = await Assert.That(publishedAt).IsNotNull();
            _ = await Assert.That(publishedAt!.Value).IsGreaterThanOrEqualTo(beforePublish);
            _ = await Assert.That(publishedAt.Value).IsLessThanOrEqualTo(afterPublish);
        }
    }

    [Test]
    public async Task QueryAsync_WithNullQuery_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            "query",
            async () => await mediator.QueryAsync<TestQuery, string>(null!, cancellationToken).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task QueryAsync_WithNoHandler_ThrowsInvalidOperationException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var query = new TestQuery();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.QueryAsync<TestQuery, string>(query, cancellationToken).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task QueryAsync_WithHandler_InvokesHandlerAndReturnsResult(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler = new TestQueryHandler("test-result");
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IQueryHandler<TestQuery, string>>(handler);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var query = new TestQuery();

        var result = await mediator.QueryAsync<TestQuery, string>(query, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert.That(handler.HandledQueries).HasSingleItem();
            _ = await Assert.That(handler.HandledQueries[0]).IsSameReferenceAs(query);
        }
    }

    [Test]
    public async Task SendAsync_WithNullCommand_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            "command",
            async () => await mediator.SendAsync<TestCommand, string>(null!, cancellationToken).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendAsync_WithNoHandler_ThrowsInvalidOperationException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var command = new TestCommand();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync<TestCommand, string>(command, cancellationToken).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendAsync_WithHandler_InvokesHandlerAndReturnsResult(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler = new TestCommandHandler("test-result");
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<ICommandHandler<TestCommand, string>>(handler);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var command = new TestCommand();

        var result = await mediator.SendAsync<TestCommand, string>(command, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert.That(handler.HandledCommands).HasSingleItem();
            _ = await Assert.That(handler.HandledCommands[0]).IsSameReferenceAs(command);
        }
    }

    [Test]
    public async Task SendAsync_WithInterceptor_InvokesInterceptorBeforeHandler(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler = new TestCommandHandler("test-result");
        var interceptor = new TestCommandInterceptor();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<ICommandHandler<TestCommand, string>>(handler);
        _ = services.AddSingleton<IRequestInterceptor<TestCommand, string>>(interceptor);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var command = new TestCommand();

        var result = await mediator.SendAsync<TestCommand, string>(command, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert.That(interceptor.InterceptedCommands).HasSingleItem();
            _ = await Assert.That(handler.HandledCommands).HasSingleItem();
        }
    }

    [Test]
    public async Task QueryAsync_WithInterceptor_InvokesInterceptorBeforeHandler(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler = new TestQueryHandler("test-result");
        var interceptor = new TestQueryInterceptor();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IQueryHandler<TestQuery, string>>(handler);
        _ = services.AddSingleton<IRequestInterceptor<TestQuery, string>>(interceptor);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var query = new TestQuery();

        var result = await mediator.QueryAsync<TestQuery, string>(query, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert.That(interceptor.InterceptedQueries).HasSingleItem();
            _ = await Assert.That(handler.HandledQueries).HasSingleItem();
        }
    }

    [Test]
    public async Task PublishAsync_WithInterceptor_InvokesInterceptorBeforeHandlers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handler = new TestEventHandler();
        var interceptor = new TestEventInterceptor();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IEventHandler<TestEvent>>(handler);
        _ = services.AddSingleton<IEventInterceptor<TestEvent>>(interceptor);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var testEvent = new TestEvent();

        await mediator.PublishAsync(testEvent, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(interceptor.InterceptedEvents).HasSingleItem();
            _ = await Assert.That(handler.HandledEvents).HasSingleItem();
        }
    }

    [Test]
    public async Task PublishAsync_WithScopedHandler_ResolvesHandlersFromCallerScope(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collector = new ScopedDependencyCollector();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton(collector);
        _ = services.AddScoped<ScopedDependency>();
        _ = services.AddScoped<IEventHandler<TestEvent>, ScopedDependencyCapturingHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<PulseMediator>>();
            var mediator = new PulseMediator(logger, scope.ServiceProvider, TimeProvider.System);
            var callerDependency = scope.ServiceProvider.GetRequiredService<ScopedDependency>();

            await mediator.PublishAsync(new TestEvent(), cancellationToken).ConfigureAwait(false);

            // The handler must observe the same scoped dependency instance as the caller's scope,
            // so that e.g. an outbox write shares the caller's DbContext and its transaction.
            _ = await Assert.That(collector.Captured).IsSameReferenceAs(callerDependency);
        }
    }

    private sealed class ScopedDependency
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class ScopedDependencyCollector
    {
        public ScopedDependency? Captured { get; set; }
    }

    private sealed class ScopedDependencyCapturingHandler : IEventHandler<TestEvent>
    {
        private readonly ScopedDependency _dependency;
        private readonly ScopedDependencyCollector _collector;

        [SuppressMessage(
            "Major Code Smell",
            "S1144:Unused private types or members should be removed",
            Justification = "Handler is resolved by the mediator through dependency injection."
        )]
        public ScopedDependencyCapturingHandler(ScopedDependency dependency, ScopedDependencyCollector collector)
        {
            _dependency = dependency;
            _collector = collector;
        }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _collector.Captured = _dependency;
            return Task.CompletedTask;
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();

        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class TestEventHandler : IEventHandler<TestEvent>
    {
        public List<TestEvent> HandledEvents { get; } = [];

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HandledEvents.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Test exception");
    }

    private sealed class TestCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class TestCommandHandler : ICommandHandler<TestCommand, string>
    {
        private readonly string _result;
        public List<TestCommand> HandledCommands { get; } = [];

        public TestCommandHandler(string result) => _result = result;

        public Task<string> HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HandledCommands.Add(command);
            return Task.FromResult(_result);
        }
    }

    private sealed class TestQuery : IQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        private readonly string _result;
        public List<TestQuery> HandledQueries { get; } = [];

        public TestQueryHandler(string result) => _result = result;

        public Task<string> HandleAsync(TestQuery request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HandledQueries.Add(request);
            return Task.FromResult(_result);
        }
    }

    private sealed class TestCommandInterceptor : IRequestInterceptor<TestCommand, string>
    {
        public List<TestCommand> InterceptedCommands { get; } = [];

        public async Task<string> HandleAsync(
            TestCommand request,
            Func<TestCommand, CancellationToken, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            InterceptedCommands.Add(request);
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TestQueryInterceptor : IRequestInterceptor<TestQuery, string>
    {
        public List<TestQuery> InterceptedQueries { get; } = [];

        public async Task<string> HandleAsync(
            TestQuery request,
            Func<TestQuery, CancellationToken, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            InterceptedQueries.Add(request);
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TestEventInterceptor : IEventInterceptor<TestEvent>
    {
        public List<TestEvent> InterceptedEvents { get; } = [];

        public async Task HandleAsync(
            TestEvent message,
            Func<TestEvent, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            InterceptedEvents.Add(message);
            await handler(message, cancellationToken).ConfigureAwait(false);
        }
    }

    // Streaming query test helpers
    private sealed class TestStreamQuery : IStreamQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class TestStreamQueryHandler : IStreamQueryHandler<TestStreamQuery, string>
    {
        private readonly IEnumerable<string> _items;
        public List<TestStreamQuery> HandledQueries { get; } = [];

        public TestStreamQueryHandler(IEnumerable<string> items) => _items = items;

        public async IAsyncEnumerable<string> HandleAsync(
            TestStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            HandledQueries.Add(request);
            foreach (var item in _items)
            {
                yield return item;
            }
        }
    }

    private sealed class TestStreamQueryInterceptor : IStreamQueryInterceptor<TestStreamQuery, string>
    {
        public List<TestStreamQuery> InterceptedQueries { get; } = [];

        public async IAsyncEnumerable<string> HandleAsync(
            TestStreamQuery request,
            Func<TestStreamQuery, CancellationToken, IAsyncEnumerable<string>> handler,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            InterceptedQueries.Add(request);
            await foreach (
                var item in handler(request, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                yield return item;
            }
        }
    }

    // StreamQueryAsync tests
    [Test]
    public async Task StreamQueryAsync_WithNullQuery_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);

        _ = Assert.Throws<ArgumentNullException>(
            "query",
            () => mediator.StreamQueryAsync<TestStreamQuery, string>(null!, cancellationToken)
        );
    }

    [Test]
    public async Task StreamQueryAsync_WithNoHandler_ThrowsInvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var query = new TestStreamQuery();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (
                var _ in mediator
                    .StreamQueryAsync<TestStreamQuery, string>(query, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                // consume
            }
        });
    }

    [Test]
    public async Task StreamQueryAsync_WithHandler_YieldsAllItems(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expectedItems = new[] { "first", "second", "third" };
        var handler = new TestStreamQueryHandler(expectedItems);
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IStreamQueryHandler<TestStreamQuery, string>>(handler);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var query = new TestStreamQuery();

        var results = new List<string>();
        await foreach (
            var item in mediator
                .StreamQueryAsync<TestStreamQuery, string>(query, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            results.Add(item);
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(results).IsEquivalentTo(expectedItems);
            _ = await Assert.That(handler.HandledQueries).HasSingleItem();
        }
    }

    [Test]
    public async Task StreamQueryAsync_WithInterceptor_InvokesInterceptorAndYieldsAllItems(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expectedItems = new[] { "alpha", "beta" };
        var handler = new TestStreamQueryHandler(expectedItems);
        var interceptor = new TestStreamQueryInterceptor();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IStreamQueryHandler<TestStreamQuery, string>>(handler);
        _ = services.AddSingleton<IStreamQueryInterceptor<TestStreamQuery, string>>(interceptor);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var query = new TestStreamQuery();

        var results = new List<string>();
        await foreach (
            var item in mediator
                .StreamQueryAsync<TestStreamQuery, string>(query, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            results.Add(item);
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(results).IsEquivalentTo(expectedItems);
            _ = await Assert.That(interceptor.InterceptedQueries).HasSingleItem();
            _ = await Assert.That(handler.HandledQueries).HasSingleItem();
        }
    }

    [Test]
    public async Task SendAsync_WithMultipleInterceptors_InvokesThemInRegistrationOrder(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var log = new List<string>();
        var handler = new TestCommandHandler("test-result");
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<ICommandHandler<TestCommand, string>>(handler);
        _ = services.AddSingleton<IRequestInterceptor<TestCommand, string>>(
            new OrderTrackingCommandInterceptor("first", log)
        );
        _ = services.AddSingleton<IRequestInterceptor<TestCommand, string>>(
            new OrderTrackingCommandInterceptor("second", log)
        );
        _ = services.AddSingleton<IRequestInterceptor<TestCommand, string>>(
            new OrderTrackingCommandInterceptor("third", log)
        );
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var command = new TestCommand();

        var result = await mediator.SendAsync<TestCommand, string>(command, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert
                .That(string.Join(",", log))
                .IsEqualTo("first:before,second:before,third:before,third:after,second:after,first:after");
        }
    }

    private sealed class OrderTrackingCommandInterceptor : IRequestInterceptor<TestCommand, string>
    {
        private readonly string _name;
        private readonly List<string> _log;

        public OrderTrackingCommandInterceptor(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public async Task<string> HandleAsync(
            TestCommand request,
            Func<TestCommand, CancellationToken, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            _log.Add($"{_name}:before");
            var result = await handler(request, cancellationToken).ConfigureAwait(false);
            _log.Add($"{_name}:after");
            return result;
        }
    }
}
