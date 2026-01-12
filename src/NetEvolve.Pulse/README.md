# NetEvolve.Pulse

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.svg)](https://www.nuget.org/packages/NetEvolve.Pulse/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.svg)](https://www.nuget.org/packages/NetEvolve.Pulse/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse is a high-performance CQRS mediator for ASP.NET Core that wires commands, queries, and events through a scoped, interceptor-enabled pipeline.

## Features

* Typed CQRS mediator with single-handler enforcement for commands and queries plus fan-out events
* Minimal DI integration via `services.AddPulse(...)` with scoped lifetimes for handlers and interceptors
* Configurable interceptor pipeline (logging, metrics, tracing, validation) via `IMediatorConfigurator`
* **Outbox pattern** with background processor for reliable event delivery via `AddOutbox()`
* Parallel event dispatch for efficient domain event broadcasting
* TimeProvider-aware for deterministic testing and scheduling scenarios
* OpenTelemetry-friendly metrics and tracing through `AddActivityAndMetrics()`

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse" Version="x.x.x" />
```

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

var services = new ServiceCollection();

// Register Pulse and handlers
services.AddPulse();
services.AddScoped<ICommandHandler<CreateOrderCommand, OrderCreated>, CreateOrderHandler>();

using var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

var result = await mediator.SendAsync<CreateOrderCommand, OrderCreated>(
    new CreateOrderCommand("SKU-123"));

Console.WriteLine($"Created order {result.OrderId}");

public record CreateOrderCommand(string Sku) : ICommand<OrderCreated>;
public record OrderCreated(Guid OrderId);

public sealed class CreateOrderHandler
    : ICommandHandler<CreateOrderCommand, OrderCreated>
{
    public Task<OrderCreated> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(new OrderCreated(Guid.NewGuid()));
}
```

## Usage

### Basic Example

```csharp
services.AddPulse();
services.AddScoped<IQueryHandler<GetOrderQuery, Order>, GetOrderHandler>();
services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();

var order = await mediator.QueryAsync<GetOrderQuery, Order>(new GetOrderQuery(orderId));
await mediator.PublishAsync(new OrderCreatedEvent(order.Id));

public record GetOrderQuery(Guid Id) : IQuery<Order>;
public record Order(Guid Id, string Sku);
public record OrderCreatedEvent(Guid Id) : IEvent;

public sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, Order>
{
    public Task<Order> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new Order(query.Id, "SKU-123"));
}

public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // React to the event (logging, projections, etc.)
        return Task.CompletedTask;
    }
}
```

### Advanced Example

```csharp
// Enable tracing and metrics and add custom interceptors
services.AddPulse(config =>
{
    config.AddActivityAndMetrics();
});

services.AddScoped<ICommandHandler<ShipOrderCommand, Void>, ShipOrderHandler>();

public record ShipOrderCommand(Guid Id) : ICommand;

public sealed class ShipOrderHandler : ICommandHandler<ShipOrderCommand, Void>
{
    public Task<Void> HandleAsync(ShipOrderCommand command, CancellationToken cancellationToken)
    {
        // Shipping workflow here
        return Task.FromResult(Void.Completed);
    }
}
```

## Configuration

```csharp
// Configure Pulse during startup
services.AddPulse(config =>
{
    // Built-in observability
    config.AddActivityAndMetrics();

    // Add your own configurator extensions for validation, caching, etc.
    // config.AddCustomValidation();
});
```

### Outbox Pattern Configuration

The outbox pattern ensures reliable event delivery by persisting events before dispatching:

```csharp
services.AddPulse(config => config
    .AddOutbox(
        options => options.Schema = "pulse",
        processorOptions =>
        {
            processorOptions.BatchSize = 100;              // Messages per batch (default: 100)
            processorOptions.PollingInterval = TimeSpan.FromSeconds(5);  // Poll delay (default: 5s)
            processorOptions.MaxRetryCount = 3;            // Max retries before dead letter (default: 3)
            processorOptions.ProcessingTimeout = TimeSpan.FromSeconds(30); // Per-message timeout (default: 30s)
            processorOptions.EnableBatchSending = false;   // Use batch transport (default: false)
        })
    // Choose a persistence provider:
    // .AddEntityFrameworkOutbox<MyDbContext>()
    // .AddSqlServerOutbox(connectionString)
);
```

See [NetEvolve.Pulse.EntityFramework](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) or [NetEvolve.Pulse.SqlServer](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) for persistence provider setup.

## Requirements

* .NET 8.0, .NET 9.0, or .NET 10.0
* ASP.NET Core environment with `Microsoft.Extensions.DependencyInjection`
* OpenTelemetry packages when using `AddActivityAndMetrics()`

## Related Packages

* [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Core contracts and abstractions used by the mediator
* [**NetEvolve.Pulse.EntityFramework**](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework Core persistence for the outbox pattern
* [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) - SQL Server ADO.NET persistence for the outbox pattern
* [**NetEvolve.Pulse.Polly**](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/) - Polly v8 resilience policies integration

## Documentation

For complete documentation, please visit the [official documentation](https://github.com/dailydevops/pulse/blob/main/README.md).

## Contributing

Contributions are welcome! Please read the [Contributing Guidelines](https://github.com/dailydevops/pulse/blob/main/CONTRIBUTING.md) before submitting a pull request.

## Support

* **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/dailydevops/pulse/issues)
* **Documentation**: Read the full documentation at [https://github.com/dailydevops/pulse](https://github.com/dailydevops/pulse)

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

---

> [!NOTE] 
> **Made with ❤️ by the NetEvolve Team**