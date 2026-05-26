// Verbatim copy of README.md lines 118-136 (the "Quick Use" snippet).
// Do not modify — this file exists to prove the snippet compiles or fails as the audit claims.

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

var services = new ServiceCollection();

services.AddPulse(config => config.AddActivityAndMetrics());
services.AddScoped<ICommandHandler<CreateOrder, OrderCreated>, CreateOrderHandler>();

public record CreateOrder(string Sku) : ICommand<OrderCreated>;

public record OrderCreated(Guid OrderId);

public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, OrderCreated>
{
    public Task<OrderCreated> HandleAsync(CreateOrder command, CancellationToken cancellationToken) =>
        Task.FromResult(new OrderCreated(Guid.NewGuid()));
}
