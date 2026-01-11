namespace NetEvolve.Pulse.Tests.Integration;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

public sealed class ServiceCollectionExtensionsTests
{
    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        return services;
    }

    [Test]
    public async Task AddPulse_WithHandlers_ResolvesAndExecutesCommandHandler()
    {
        var services = CreateServiceCollection();
        _ = services.AddPulse().AddScoped<ICommandHandler<TestCommand, TestResponse>, TestCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new TestCommand("Test");
        var response = await mediator.SendAsync<TestCommand, TestResponse>(command);

        _ = await Assert.That(response).IsNotNull();
        _ = await Assert.That(response.Value).IsEqualTo("Test");
    }

    [Test]
    public async Task AddPulse_WithHandlers_ResolvesAndExecutesQueryHandler()
    {
        var services = CreateServiceCollection();
        _ = services.AddPulse().AddScoped<IQueryHandler<TestQuery, TestResponse>, TestQueryHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new TestQuery("Query");
        var response = await mediator.QueryAsync<TestQuery, TestResponse>(query);

        _ = await Assert.That(response).IsNotNull();
        _ = await Assert.That(response.Value).IsEqualTo("Query");
    }

    [Test]
    public async Task AddPulse_WithHandlers_ResolvesAndExecutesEventHandlers()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse()
            .AddScoped<IEventHandler<TestEvent>, TestEventHandler>()
            .AddScoped<IEventHandler<TestEvent>, SecondTestEventHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var handler1 = scope
            .ServiceProvider.GetServices<IEventHandler<TestEvent>>()
            .OfType<TestEventHandler>()
            .Single();
        var handler2 = scope
            .ServiceProvider.GetServices<IEventHandler<TestEvent>>()
            .OfType<SecondTestEventHandler>()
            .Single();

        var evt = new TestEvent("Event");
        await mediator.PublishAsync(evt);

        using (Assert.Multiple())
        {
            _ = await Assert.That(handler1.Handled).IsTrue();
            _ = await Assert.That(handler2.Handled).IsTrue();
        }
    }

    [Test]
    public async Task AddPulse_WithActivityAndMetrics_RegistersInterceptors()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddActivityAndMetrics())
            .AddScoped<ICommandHandler<TestCommand, TestResponse>, TestCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new TestCommand("Test");
        var response = await mediator.SendAsync<TestCommand, TestResponse>(command);

        _ = await Assert.That(response).IsNotNull();
    }

    [Test]
    public async Task AddPulse_WithMultipleScopes_HandlerHasCorrectLifetime()
    {
        var services = CreateServiceCollection();
        _ = services.AddPulse().AddScoped<ICommandHandler<TestCommand, TestResponse>, TestCommandHandler>();

        await using var provider = services.BuildServiceProvider();

        TestResponse response1;
        await using (var scope1 = provider.CreateAsyncScope())
        {
            var mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
            response1 = await mediator1.SendAsync<TestCommand, TestResponse>(new TestCommand("Scope1"));
        }

        TestResponse response2;
        await using (var scope2 = provider.CreateAsyncScope())
        {
            var mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();
            response2 = await mediator2.SendAsync<TestCommand, TestResponse>(new TestCommand("Scope2"));
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(response1.Value).IsEqualTo("Scope1");
            _ = await Assert.That(response2.Value).IsEqualTo("Scope2");
        }
    }

    [Test]
    public async Task AddPulse_WithInterceptor_ExecutesInterceptorPipeline()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse()
            .AddScoped<ICommandHandler<TestCommand, TestResponse>, TestCommandHandler>()
            .AddSingleton<IRequestInterceptor<TestCommand, TestResponse>, TestCommandInterceptor>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var interceptor =
            provider.GetRequiredService<IRequestInterceptor<TestCommand, TestResponse>>() as TestCommandInterceptor;

        var command = new TestCommand("Intercepted");
        var response = await mediator.SendAsync<TestCommand, TestResponse>(command);

        using (Assert.Multiple())
        {
            _ = await Assert.That(interceptor).IsNotNull();
            _ = await Assert.That(interceptor!.Executed).IsTrue();
            _ = await Assert.That(response.Value).IsEqualTo("Intercepted");
        }
    }

    [Test]
    public void AddPulse_MissingHandler_ThrowsInvalidOperationException()
    {
        var services = CreateServiceCollection();
        _ = services.AddPulse();

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new TestCommand("NoHandler");

        _ = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync<TestCommand, TestResponse>(command)
        );
    }

    private sealed record TestCommand(string Value) : ICommand<TestResponse>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestQuery(string Value) : IQuery<TestResponse>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestResponse(string Value);

    private sealed class TestEvent : IEvent
    {
        public TestEvent(string value) => Value = value;

        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
        public string Value { get; }
    }

    private sealed class TestCommandHandler : ICommandHandler<TestCommand, TestResponse>
    {
        public Task<TestResponse> HandleAsync(TestCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TestResponse(command.Value));
    }

    private sealed class TestQueryHandler : IQueryHandler<TestQuery, TestResponse>
    {
        public Task<TestResponse> HandleAsync(TestQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TestResponse(query.Value));
    }

    private sealed class TestEventHandler : IEventHandler<TestEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class SecondTestEventHandler : IEventHandler<TestEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCommandInterceptor : IRequestInterceptor<TestCommand, TestResponse>
    {
        public bool Executed { get; private set; }

        public async Task<TestResponse> HandleAsync(
            TestCommand request,
            Func<TestCommand, Task<TestResponse>> next,
            CancellationToken cancellationToken = default
        )
        {
            Executed = true;
            return await next(request);
        }
    }
}
