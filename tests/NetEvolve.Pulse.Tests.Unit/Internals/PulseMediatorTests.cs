namespace NetEvolve.Pulse.Tests.Unit.Internals;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

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
    public async Task PublishAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            "message",
            async () => await mediator.PublishAsync<TestEvent>(null!).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task PublishAsync_WithNoHandlers_CompletesSuccessfully()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var testEvent = new TestEvent();

        await mediator.PublishAsync(testEvent).ConfigureAwait(false);

        _ = await Assert.That(testEvent.PublishedAt).IsNotNull();
    }

    [Test]
    public async Task PublishAsync_WithHandlers_InvokesAllHandlers()
    {
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

        await mediator.PublishAsync(testEvent).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(handler1.HandledEvents).HasSingleItem();
            _ = await Assert.That(handler2.HandledEvents).HasSingleItem();
            _ = await Assert.That(handler1.HandledEvents[0]).IsSameReferenceAs(testEvent);
            _ = await Assert.That(handler2.HandledEvents[0]).IsSameReferenceAs(testEvent);
        }
    }

    [Test]
    public async Task PublishAsync_WithHandlerException_ContinuesExecutingOtherHandlersAndThrowsAggregate()
    {
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
            await mediator.PublishAsync(testEvent).ConfigureAwait(false)
        );

        // Verify the exception contains the handler failure
        _ = await Assert.That(exception!.InnerExceptions).Count().IsEqualTo(1);
        _ = await Assert.That(exception.InnerExceptions[0]).IsAssignableTo<InvalidOperationException>();

        // Verify the successful handler still executed despite the failure
        _ = await Assert.That(handler2.HandledEvents).HasSingleItem();
    }

    [Test]
    public async Task PublishAsync_SetsPublishedAtTimestamp()
    {
        var services = new ServiceCollection().AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var testEvent = new TestEvent();
        var beforePublish = timeProvider.GetUtcNow();

        await mediator.PublishAsync(testEvent).ConfigureAwait(false);

        var afterPublish = timeProvider.GetUtcNow();
        var publishedAt = testEvent.PublishedAt;
        using (Assert.Multiple())
        {
            _ = await Assert.That(publishedAt).IsNotNull();
            _ = await Assert.That(publishedAt!.Value).IsGreaterThanOrEqualTo(beforePublish);
            _ = await Assert.That(publishedAt!.Value).IsLessThanOrEqualTo(afterPublish);
        }
    }

    [Test]
    public async Task QueryAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            "query",
            async () => await mediator.QueryAsync<TestQuery, string>(null!).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task QueryAsync_WithNoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var query = new TestQuery();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.QueryAsync<TestQuery, string>(query).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task QueryAsync_WithHandler_InvokesHandlerAndReturnsResult()
    {
        var handler = new TestQueryHandler("test-result");
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IQueryHandler<TestQuery, string>>(handler);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var query = new TestQuery();

        var result = await mediator.QueryAsync<TestQuery, string>(query).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert.That(handler.HandledQueries).HasSingleItem();
            _ = await Assert.That(handler.HandledQueries[0]).IsSameReferenceAs(query);
        }
    }

    [Test]
    public async Task SendAsync_WithNullCommand_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            "command",
            async () => await mediator.SendAsync<TestCommand, string>(null!).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendAsync_WithNoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var command = new TestCommand();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync<TestCommand, string>(command).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendAsync_WithHandler_InvokesHandlerAndReturnsResult()
    {
        var handler = new TestCommandHandler("test-result");
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<ICommandHandler<TestCommand, string>>(handler);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PulseMediator>>();
        var timeProvider = TimeProvider.System;
        var mediator = new PulseMediator(logger, serviceProvider, timeProvider);
        var command = new TestCommand();

        var result = await mediator.SendAsync<TestCommand, string>(command).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert.That(handler.HandledCommands).HasSingleItem();
            _ = await Assert.That(handler.HandledCommands[0]).IsSameReferenceAs(command);
        }
    }

    [Test]
    public async Task SendAsync_WithInterceptor_InvokesInterceptorBeforeHandler()
    {
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

        var result = await mediator.SendAsync<TestCommand, string>(command).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert.That(interceptor.InterceptedCommands).HasSingleItem();
            _ = await Assert.That(handler.HandledCommands).HasSingleItem();
        }
    }

    [Test]
    public async Task QueryAsync_WithInterceptor_InvokesInterceptorBeforeHandler()
    {
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

        var result = await mediator.QueryAsync<TestQuery, string>(query).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert.That(interceptor.InterceptedQueries).HasSingleItem();
            _ = await Assert.That(handler.HandledQueries).HasSingleItem();
        }
    }

    [Test]
    public async Task PublishAsync_WithInterceptor_InvokesInterceptorBeforeHandlers()
    {
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

        await mediator.PublishAsync(testEvent).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(interceptor.InterceptedEvents).HasSingleItem();
            _ = await Assert.That(handler.HandledEvents).HasSingleItem();
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();

        public string? CorrelationId { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class TestEventHandler : IEventHandler<TestEvent>
    {
        public List<TestEvent> HandledEvents { get; } = [];

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
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
        public string? CorrelationId { get; set; }
    }

    private sealed class TestCommandHandler : ICommandHandler<TestCommand, string>
    {
        private readonly string _result;
        public List<TestCommand> HandledCommands { get; } = [];

        public TestCommandHandler(string result) => _result = result;

        public Task<string> HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            HandledCommands.Add(command);
            return Task.FromResult(_result);
        }
    }

    private sealed class TestQuery : IQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        private readonly string _result;
        public List<TestQuery> HandledQueries { get; } = [];

        public TestQueryHandler(string result) => _result = result;

        public Task<string> HandleAsync(TestQuery request, CancellationToken cancellationToken = default)
        {
            HandledQueries.Add(request);
            return Task.FromResult(_result);
        }
    }

    private sealed class TestCommandInterceptor : IRequestInterceptor<TestCommand, string>
    {
        public List<TestCommand> InterceptedCommands { get; } = [];

        public async Task<string> HandleAsync(
            TestCommand request,
            Func<TestCommand, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            InterceptedCommands.Add(request);
            return await handler(request).ConfigureAwait(false);
        }
    }

    private sealed class TestQueryInterceptor : IRequestInterceptor<TestQuery, string>
    {
        public List<TestQuery> InterceptedQueries { get; } = [];

        public async Task<string> HandleAsync(
            TestQuery request,
            Func<TestQuery, Task<string>> handler,
            CancellationToken cancellationToken = default
        )
        {
            InterceptedQueries.Add(request);
            return await handler(request).ConfigureAwait(false);
        }
    }

    private sealed class TestEventInterceptor : IEventInterceptor<TestEvent>
    {
        public List<TestEvent> InterceptedEvents { get; } = [];

        public async Task HandleAsync(
            TestEvent message,
            Func<TestEvent, Task> handler,
            CancellationToken cancellationToken = default
        )
        {
            InterceptedEvents.Add(message);
            await handler(message).ConfigureAwait(false);
        }
    }
}
