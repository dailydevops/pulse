namespace NetEvolve.Pulse.Tests.Integration.AspNetCore;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using Void = Extensibility.Void;

public sealed class MinimalApiTests
{
    [Test]
    public async Task MapCommand_WithResponse_ReturnsOkWithResult()
    {
        await using var app = CreateApp(
            endpoints => endpoints.MapCommand<CreateOrderCommand, OrderResult>("/orders"),
            services =>
                services.AddScoped<ICommandHandler<CreateOrderCommand, OrderResult>, CreateOrderCommandHandler>()
        );

        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/orders", new CreateOrderCommand("order-123", 99.99m));

        using (Assert.Multiple())
        {
            _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<OrderResult>();
            _ = await Assert.That(result).IsNotNull();
            _ = await Assert.That(result!.OrderId).IsEqualTo("order-123");
            _ = await Assert.That(result.Total).IsEqualTo(99.99m);
        }
    }

    [Test]
    public async Task MapCommand_WithResponse_PutMethod_ReturnsOkWithResult()
    {
        await using var app = CreateApp(
            endpoints => endpoints.MapCommand<CreateOrderCommand, OrderResult>("/orders/put", CommandHttpMethod.Put),
            services =>
                services.AddScoped<ICommandHandler<CreateOrderCommand, OrderResult>, CreateOrderCommandHandler>()
        );

        await app.StartAsync();
        var client = app.GetTestClient();

        using var content = JsonContent.Create(new CreateOrderCommand("order-put", 55.00m));
        var response = await client.PutAsync(new Uri("/orders/put", UriKind.Relative), content);

        using (Assert.Multiple())
        {
            _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<OrderResult>();
            _ = await Assert.That(result).IsNotNull();
            _ = await Assert.That(result!.OrderId).IsEqualTo("order-put");
        }
    }

    [Test]
    public async Task MapCommand_Void_ReturnsNoContent()
    {
        await using var app = CreateApp(
            endpoints => endpoints.MapCommand<DeleteOrderCommand>("/orders/delete"),
            services => services.AddScoped<ICommandHandler<DeleteOrderCommand, Void>, DeleteOrderCommandHandler>()
        );

        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/orders/delete", new DeleteOrderCommand("order-123"));

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task MapCommand_Void_DeleteMethod_ReturnsNoContent()
    {
        await using var app = CreateApp(
            endpoints => endpoints.MapCommand<DeleteOrderCommand>("/orders/{orderId}", CommandHttpMethod.Delete),
            services => services.AddScoped<ICommandHandler<DeleteOrderCommand, Void>, DeleteOrderCommandHandler>()
        );

        await app.StartAsync();
        var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri("/orders/order-789", UriKind.Relative))
        {
            Content = JsonContent.Create(new DeleteOrderCommand("order-789")),
        };
        var response = await client.SendAsync(request);

        _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task MapQuery_ReturnsOkWithResult()
    {
        await using var app = CreateApp(
            endpoints => endpoints.MapQuery<GetOrderQuery, OrderDto>("/orders/{orderId}"),
            services => services.AddScoped<IQueryHandler<GetOrderQuery, OrderDto>, GetOrderQueryHandler>()
        );

        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync(new Uri("/orders/order-456", UriKind.Relative));

        using (Assert.Multiple())
        {
            _ = await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<OrderDto>();
            _ = await Assert.That(result).IsNotNull();
            _ = await Assert.That(result!.OrderId).IsEqualTo("order-456");
            _ = await Assert.That(result.Status).IsEqualTo("Active");
        }
    }

    [Test]
    public async Task MapCommand_WithResponse_PropagatesCancellationToken()
    {
        await using var app = CreateApp(
            endpoints => endpoints.MapCommand<SlowCommand, string>("/slow"),
            services => services.AddScoped<ICommandHandler<SlowCommand, string>, SlowCommandHandler>()
        );

        await app.StartAsync();
        var client = app.GetTestClient();
        client.Timeout = TimeSpan.FromMilliseconds(100);

        _ = await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await client.PostAsJsonAsync("/slow", new SlowCommand())
        );
    }

    private static WebApplication CreateApp(
        Action<WebApplication> configureRoutes,
        Action<IServiceCollection> configureServices
    )
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.WebHost.UseTestServer();

        _ = builder.Services.AddPulse();
        configureServices(builder.Services);

        var app = builder.Build();
        configureRoutes(app);

        return app;
    }

    private sealed record CreateOrderCommand(string OrderId, decimal Total) : ICommand<OrderResult>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record DeleteOrderCommand(string OrderId) : ICommand
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record GetOrderQuery(string OrderId) : IQuery<OrderDto>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record SlowCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record OrderResult(string OrderId, decimal Total);

    private sealed record OrderDto(string OrderId, string Status);

    private sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, OrderResult>
    {
        public Task<OrderResult> HandleAsync(
            CreateOrderCommand command,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new OrderResult(command.OrderId, command.Total));
    }

    private sealed class DeleteOrderCommandHandler : ICommandHandler<DeleteOrderCommand, Void>
    {
        public Task<Void> HandleAsync(DeleteOrderCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(Void.Completed);
    }

    private sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderDto>
    {
        public Task<OrderDto> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderDto(query.OrderId, "Active"));
    }

    private sealed class SlowCommandHandler : ICommandHandler<SlowCommand, string>
    {
        public async Task<string> HandleAsync(SlowCommand command, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return "done";
        }
    }
}
