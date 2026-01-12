namespace NetEvolve.Pulse.Tests.Integration;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using Void = Extensibility.Void;

public sealed class PulseMediatorTests
{
    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        return services;
    }

    [Test]
    public async Task SendAsync_WithCommand_ExecutesHandler()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse()
            .AddScoped<ICommandHandler<CreateOrderCommand, OrderResult>, CreateOrderCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new CreateOrderCommand("Order123", 100.50m);
        var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(command).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsNotNull();
            _ = await Assert.That(result.OrderId).IsEqualTo("Order123");
            _ = await Assert.That(result.Total).IsEqualTo(100.50m);
            _ = await Assert.That(result.Success).IsTrue();
        }
    }

    [Test]
    public async Task SendAsync_WithVoidCommand_ExecutesHandler()
    {
        var services = CreateServiceCollection();
        _ = services.AddPulse().AddScoped<ICommandHandler<DeleteOrderCommand, Void>, DeleteOrderCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new DeleteOrderCommand("Order123");
        var result = await mediator.SendAsync<DeleteOrderCommand, Void>(command).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo(default(Void));
    }

    [Test]
    public async Task QueryAsync_WithQuery_ExecutesHandler()
    {
        var services = CreateServiceCollection();
        _ = services.AddPulse().AddScoped<IQueryHandler<GetOrderQuery, OrderDto>, GetOrderQueryHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new GetOrderQuery("Order456");
        var result = await mediator.QueryAsync<GetOrderQuery, OrderDto>(query).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsNotNull();
            _ = await Assert.That(result.Id).IsEqualTo("Order456");
            _ = await Assert.That(result.Status).IsEqualTo("Completed");
        }
    }

    [Test]
    public async Task PublishAsync_WithEvent_ExecutesAllHandlers()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse()
            .AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>()
            .AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedEmailHandler>()
            .AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedNotificationHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<OrderCreatedEvent>>().ToList();

        var evt = new OrderCreatedEvent("Order789", DateTimeOffset.UtcNow);
        await mediator.PublishAsync(evt).ConfigureAwait(false);

        await Task.Delay(100).ConfigureAwait(false); // Give handlers time to complete

        foreach (var handler in handlers)
        {
            if (handler is OrderCreatedEventHandler h1)
            {
                _ = await Assert.That(h1.Handled).IsTrue();
            }
            else if (handler is OrderCreatedEmailHandler h2)
            {
                _ = await Assert.That(h2.Handled).IsTrue();
            }
            else if (handler is OrderCreatedNotificationHandler h3)
            {
                _ = await Assert.That(h3.Handled).IsTrue();
            }
        }
    }

    [Test]
    public async Task PublishAsync_WithEvent_SetsPublishedAt()
    {
        var services = CreateServiceCollection();
        _ = services.AddPulse().AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var evt = new OrderCreatedEvent("Order999", DateTimeOffset.UtcNow);
        var beforePublish = DateTimeOffset.UtcNow;
        await mediator.PublishAsync(evt).ConfigureAwait(false);
        var afterPublish = DateTimeOffset.UtcNow;

        using (Assert.Multiple())
        {
            _ = await Assert.That(evt.PublishedAt).IsNotNull();
            _ = await Assert.That(evt.PublishedAt!.Value).IsGreaterThanOrEqualTo(beforePublish);
            _ = await Assert.That(evt.PublishedAt!.Value).IsLessThanOrEqualTo(afterPublish);
        }
    }

    [Test]
    public async Task PublishAsync_WithEventHandlerException_ContinuesExecutingOtherHandlersAndThrowsAggregate()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse()
            .AddScoped<IEventHandler<OrderCreatedEvent>, FailingEventHandler>()
            .AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var successfulHandler = scope
            .ServiceProvider.GetServices<IEventHandler<OrderCreatedEvent>>()
            .OfType<OrderCreatedEventHandler>()
            .Single();

        var evt = new OrderCreatedEvent("Order123", DateTimeOffset.UtcNow);

        // Act & Assert - PublishAsync throws AggregateException containing the handler failure
        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            await mediator.PublishAsync(evt).ConfigureAwait(false)
        );

        // Verify the exception contains the handler failure
        _ = await Assert.That(exception!.InnerExceptions).Count().IsEqualTo(1);
        _ = await Assert.That(exception.InnerExceptions[0]).IsAssignableTo<InvalidOperationException>();

        // Verify the successful handler still executed despite the failure
        _ = await Assert.That(successfulHandler.Handled).IsTrue();
    }

    [Test]
    public async Task SendAsync_WithCancellation_PropagatesCancellationToken()
    {
        var services = CreateServiceCollection();
        _ = services.AddPulse().AddScoped<ICommandHandler<SlowCommand, Void>, SlowCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var command = new SlowCommand();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await mediator.SendAsync<SlowCommand, Void>(command, cts.Token).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendAsync_WithInterceptorChain_ExecutesInCorrectOrder()
    {
        var services = CreateServiceCollection();
        _ = services
            .AddPulse()
            .AddScoped<ICommandHandler<OrderCommand, string>, OrderCommandHandler>()
            .AddSingleton<IRequestInterceptor<OrderCommand, string>, FirstInterceptor>()
            .AddSingleton<IRequestInterceptor<OrderCommand, string>, SecondInterceptor>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new OrderCommand();
        var result = await mediator.SendAsync<OrderCommand, string>(command).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("First->Second->Handler");
    }

    private sealed record CreateOrderCommand(string OrderId, decimal Total) : ICommand<OrderResult>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record DeleteOrderCommand(string OrderId) : ICommand<Void>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record GetOrderQuery(string OrderId) : IQuery<OrderDto>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record OrderResult(string OrderId, decimal Total, bool Success);

    private sealed record OrderDto(string Id, string Status);

    private sealed record SlowCommand : ICommand<Void>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record OrderCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, OrderResult>
    {
        public Task<OrderResult> HandleAsync(
            CreateOrderCommand command,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new OrderResult(command.OrderId, command.Total, true));
    }

    private sealed class DeleteOrderCommandHandler : ICommandHandler<DeleteOrderCommand, Void>
    {
        public Task<Void> HandleAsync(DeleteOrderCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(default(Void));
    }

    private sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderDto>
    {
        public Task<OrderDto> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderDto(query.OrderId, "Completed"));
    }

    private sealed class OrderCreatedEvent : IEvent
    {
        public OrderCreatedEvent(string orderId, DateTimeOffset createdAt)
        {
            OrderId = orderId;
            CreatedAt = createdAt;
        }

        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
        public string OrderId { get; }
        public DateTimeOffset CreatedAt { get; }
    }

    private sealed class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class OrderCreatedEmailHandler : IEventHandler<OrderCreatedEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingEventHandler : IEventHandler<OrderCreatedEvent>
    {
        public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Handler failed");
    }

    private sealed class SlowCommandHandler : ICommandHandler<SlowCommand, Void>
    {
        public async Task<Void> HandleAsync(SlowCommand command, CancellationToken cancellationToken = default)
        {
            await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            return default;
        }
    }

    private sealed class OrderCommandHandler : ICommandHandler<OrderCommand, string>
    {
        public Task<string> HandleAsync(OrderCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult("Handler");
    }

    private sealed class FirstInterceptor : IRequestInterceptor<OrderCommand, string>
    {
        public async Task<string> HandleAsync(
            OrderCommand request,
            Func<OrderCommand, Task<string>> next,
            CancellationToken cancellationToken = default
        )
        {
            var result = await next(request).ConfigureAwait(false);
            return $"First->{result}";
        }
    }

    private sealed class SecondInterceptor : IRequestInterceptor<OrderCommand, string>
    {
        public async Task<string> HandleAsync(
            OrderCommand request,
            Func<OrderCommand, Task<string>> next,
            CancellationToken cancellationToken = default
        )
        {
            var result = await next(request).ConfigureAwait(false);
            return $"Second->{result}";
        }
    }
}
