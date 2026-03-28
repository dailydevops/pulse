# NetEvolve.Pulse.AspNetCore

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.AspNetCore.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AspNetCore/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.AspNetCore.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AspNetCore/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.AspNetCore provides `IEndpointRouteBuilder` extension methods that map Pulse mediator commands and queries directly to ASP.NET Core Minimal API HTTP endpoints. Eliminate the boilerplate of endpoint lambdas that only forward to `IMediator`.

## Features

- **`MapCommand<TCommand, TResponse>`**: Maps a command to an HTTP endpoint returning `200 OK` with the response. Defaults to `POST` when no method is specified; accepts any `CommandHttpMethod` value.
- **`MapCommand<TCommand>`**: Maps a void command to an HTTP endpoint returning `204 No Content`. Defaults to `POST` when no method is specified; accepts any `CommandHttpMethod` value.
- **`MapQuery<TQuery, TResponse>`**: Maps a query to a `GET` endpoint returning `200 OK` with the result.
- **`CommandHttpMethod` enum**: Strongly-typed HTTP method selection — `Post`, `Put`, `Patch`, `Delete`. `GET` is excluded by design since commands are state-changing operations.
- **CancellationToken propagation**: Automatically propagates the HTTP request cancellation token.
- **OpenAPI compatible**: Returns typed results (`TypedResults`) so `WithOpenApi()` produces correct response schemas.
- **DI-based**: `IMediator` is resolved from the request scope at runtime — no compile-time dependency on `NetEvolve.Pulse`.

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.AspNetCore
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.AspNetCore
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.AspNetCore" Version="x.x.x" />
```

## Quick Start

```csharp
using NetEvolve.Pulse;

var builder = WebApplication.CreateBuilder(args);

// Register Pulse and handlers
builder.Services.AddPulse();
builder.Services.AddScoped<ICommandHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateOrderCommand, OrderResult>, UpdateOrderHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteOrderCommand, Void>, DeleteOrderHandler>();
builder.Services.AddScoped<IQueryHandler<GetOrderQuery, OrderDto>, GetOrderHandler>();

var app = builder.Build();

// Map commands and queries — no boilerplate lambdas needed
app.MapCommand<CreateOrderCommand, OrderResult>("/orders");                                    // POST  /orders
app.MapCommand<UpdateOrderCommand, OrderResult>("/orders/{id}", CommandHttpMethod.Put);        // PUT   /orders/{id}
app.MapCommand<DeleteOrderCommand>("/orders/{id}", CommandHttpMethod.Delete);                  // DELETE /orders/{id}
app.MapQuery<GetOrderQuery, OrderDto>("/orders/{id}");                                         // GET   /orders/{id}

app.Run();
```

Without this package you would write:

```csharp
app.MapPost("/orders", async (CreateOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
    Results.Ok(await mediator.SendAsync<CreateOrderCommand, OrderResult>(cmd, ct)));

app.MapPut("/orders/{id}", async (UpdateOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
    Results.Ok(await mediator.SendAsync<UpdateOrderCommand, OrderResult>(cmd, ct)));

app.MapDelete("/orders/{id}", async ([FromBody] DeleteOrderCommand cmd, IMediator mediator, CancellationToken ct) =>
{
    await mediator.SendAsync<DeleteOrderCommand>(cmd, ct);
    return Results.NoContent();
});

app.MapGet("/orders/{id}", async ([AsParameters] GetOrderQuery query, IMediator mediator, CancellationToken ct) =>
    Results.Ok(await mediator.QueryAsync<GetOrderQuery, OrderDto>(query, ct)));
```

## Usage

### Commands with a Response

`MapCommand<TCommand, TResponse>` binds the request body to `TCommand`, sends it via `IMediator.SendAsync`, and returns `200 OK` with the result. The default HTTP method is `POST`; use the `CommandHttpMethod` parameter to choose a different method:

```csharp
// POST /orders  (default)
app.MapCommand<CreateOrderCommand, OrderResult>("/orders");

// PUT /orders/{id}
app.MapCommand<UpdateOrderCommand, OrderResult>("/orders/{id}", CommandHttpMethod.Put);

// PATCH /orders/{id}
app.MapCommand<PatchOrderCommand, OrderResult>("/orders/{id}", CommandHttpMethod.Patch);
```

```csharp
public record CreateOrderCommand(string Sku, int Quantity) : ICommand<OrderResult>;
public record UpdateOrderCommand(Guid Id, string Sku, int Quantity) : ICommand<OrderResult>;
public record OrderResult(Guid OrderId, string Status);
```

### Void Commands

`MapCommand<TCommand>` binds the request body to `TCommand`, sends it via `IMediator.SendAsync`, and returns `204 No Content`. The default HTTP method is `POST`:

```csharp
// POST /orders/cancel  (default)
app.MapCommand<CancelOrderCommand>("/orders/cancel");

// DELETE /orders/{id}
app.MapCommand<DeleteOrderCommand>("/orders/{id}", CommandHttpMethod.Delete);
```

```csharp
public record CancelOrderCommand(Guid Id) : ICommand;
public record DeleteOrderCommand(Guid Id) : ICommand;
```

### Queries

`MapQuery<TQuery, TResponse>` registers a `GET` endpoint that binds route parameters and query string to `TQuery` using `[AsParameters]`, executes the query via `IMediator.QueryAsync`, and returns `200 OK` with the result:

```csharp
app.MapQuery<GetOrderQuery, OrderDto>("/orders/{id}");
```

```csharp
public record GetOrderQuery(Guid Id) : IQuery<OrderDto>;
public record OrderDto(Guid Id, string Sku, string Status);
```

### CommandHttpMethod Enum

The `CommandHttpMethod` enum controls the HTTP method registered for command endpoints. `GET` is intentionally excluded because commands are state-changing operations — use `MapQuery` for read-only operations instead:

| Value | HTTP Method | Typical use |
|-------|-------------|-------------|
| `Post` (default) | `POST` | Create a new resource |
| `Put` | `PUT` | Replace an existing resource |
| `Patch` | `PATCH` | Partially update a resource |
| `Delete` | `DELETE` | Remove a resource |

> [!NOTE]
> Passing an undefined enum value throws `ArgumentOutOfRangeException`.

### Chaining Endpoint Configuration

All methods return `RouteHandlerBuilder`, so you can chain Minimal API metadata:

```csharp
app.MapCommand<CreateOrderCommand, OrderResult>("/orders")
   .WithName("CreateOrder")
   .WithTags("Orders")
   .WithOpenApi()
   .RequireAuthorization();

app.MapCommand<DeleteOrderCommand>("/orders/{id}", CommandHttpMethod.Delete)
   .WithName("DeleteOrder")
   .WithTags("Orders")
   .WithOpenApi()
   .RequireAuthorization();

app.MapQuery<GetOrderQuery, OrderDto>("/orders/{id}")
   .WithName("GetOrder")
   .WithTags("Orders")
   .WithOpenApi()
   .RequireAuthorization("ReadOrders");
```

### Grouping Endpoints

Combine with `MapGroup` for shared prefixes and metadata:

```csharp
var orders = app.MapGroup("/orders")
    .WithTags("Orders")
    .RequireAuthorization();

orders.MapCommand<CreateOrderCommand, OrderResult>("/");
orders.MapCommand<UpdateOrderCommand, OrderResult>("/{id}", CommandHttpMethod.Put);
orders.MapCommand<DeleteOrderCommand>("/{id}", CommandHttpMethod.Delete);
orders.MapQuery<GetOrderQuery, OrderDto>("/{id}");
```

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- ASP.NET Core (included in the SDK)
- `NetEvolve.Pulse` (or any `IMediator` implementation) registered in the DI container

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core CQRS mediator
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Extensibility contracts
- [**NetEvolve.Pulse.Polly**](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/) - Polly v8 resilience policies integration
- [**NetEvolve.Pulse.EntityFramework**](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework Core outbox persistence
- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) - SQL Server ADO.NET outbox persistence

## Contributing

Contributions are welcome! Please read the [Contributing Guidelines](https://github.com/dailydevops/pulse/blob/main/CONTRIBUTING.md) before submitting a pull request.

## Support

- **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/dailydevops/pulse/issues)
- **Documentation**: Read the full documentation at [https://github.com/dailydevops/pulse](https://github.com/dailydevops/pulse)

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
