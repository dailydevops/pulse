# NetEvolve.Pulse.AspNetCore

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.AspNetCore.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AspNetCore/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.AspNetCore.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AspNetCore/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.AspNetCore provides `IEndpointRouteBuilder` extension methods that map Pulse mediator commands and queries directly to ASP.NET Core Minimal API HTTP endpoints. Eliminate the boilerplate of endpoint lambdas that only forward to `IMediator`.

## Features

- **`MapCommand<TCommand, TResponse>`**: Maps a command to an HTTP endpoint returning `200 OK` with the response. Defaults to `POST` when no method is specified; accepts any `CommandHttpMethod` value.
- **`MapCommand<TCommand>`**: Maps a void command to an HTTP endpoint returning `204 No Content`. Defaults to `POST` when no method is specified; accepts any `CommandHttpMethod` value.
- **`MapQuery<TQuery, TResponse>`**: Maps a query to a `GET` endpoint returning `200 OK` with the result.
- **`MapStreamQueryHub<TQuery, TResponse>`**: Maps a `PulseStreamHub` that exposes a stream query as a native SignalR server-to-client stream (requires `AddSignalR()`).
- **`CommandHttpMethod` enum**: Strongly-typed HTTP method selection — `Post`, `Put`, `Patch`, `Delete`. `GET` is excluded by design since commands are state-changing operations.
- **CancellationToken propagation**: Automatically propagates the HTTP request cancellation token.
- **OpenAPI compatible**: Returns typed results (`TypedResults`) so `WithOpenApi()` produces correct response schemas.
- **DI-based**: `IMediator` is resolved from the request scope at runtime — no compile-time dependency on `NetEvolve.Pulse`.
- **Outbox Inspector (`MapOutboxInspector`)**: Minimal API endpoints to inspect outbox messages and to replay or dismiss dead letters, backed by `IOutboxManagement`. See [Outbox Inspector](#outbox-inspector).

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

### SignalR Stream Queries

`MapStreamQueryHub<TQuery, TResponse>` maps a `PulseStreamHub<TQuery, TResponse>` to a path. Clients call the `StreamAsync` hub method as a SignalR server-to-client stream. The hub runs `IMediator.StreamQueryAsync` and sends each item to the caller as a stream item. SignalR is part of the ASP.NET Core shared framework, so you don't need an extra package on the server. You must register SignalR with `AddSignalR()` first, otherwise `MapStreamQueryHub` throws `InvalidOperationException`:

```csharp
builder.Services.AddSignalR();
// ...
app.MapStreamQueryHub<GetOrdersStreamQuery, OrderDto>("/hubs/orders");
```

```csharp
public record GetOrdersStreamQuery(string CustomerId) : IStreamQuery<OrderDto>;
```

`TQuery` must be deserializable by the configured hub protocol (JSON by default). Each query type needs its own hub path, because SignalR does not support generic hub methods.

To cancel, the client unsubscribes from the stream. SignalR then cancels the hub method's `CancellationToken`, and the hub ends the stream without an error. A disconnect cancels the stream the same way.

JavaScript client (`@microsoft/signalr`):

```javascript
const subscription = connection.stream("StreamAsync", { customerId: "42" }).subscribe({
  next: (order) => console.log(order),
  complete: () => console.log("done"),
  error: (err) => console.error(err),
});

// Cancels the server-side stream.
subscription.dispose();
```

.NET client (`Microsoft.AspNetCore.SignalR.Client`):

```csharp
using var cts = new CancellationTokenSource();

try
{
    await foreach (var order in connection.StreamAsync<OrderDto>("StreamAsync", new GetOrdersStreamQuery("42"), cts.Token))
    {
        Console.WriteLine(order);
        if (order.Status == "Shipped")
        {
            // Cancels the server-side stream.
            cts.Cancel();
        }
    }
}
catch (OperationCanceledException)
{
    // Raised on the client after cancelling; the server ends the stream without an error.
}
```

The hub accepts anonymous connections unless you apply authorization. Secure it like any other endpoint:

```csharp
app.MapStreamQueryHub<GetOrdersStreamQuery, OrderDto>("/hubs/orders").RequireAuthorization();
```

The query payload comes from the client and is untrusted. Validate it in the handler or an interceptor and scope it to the calling user (for example, check that `CustomerId` belongs to the caller).

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

### Command Dead Letter Inspector

`MapCommandDeadLetterInspector` maps administrative endpoints over the registered `ICommandDeadLetterManagement` (provided by the SQL Server, PostgreSQL, SQLite, MySQL and Entity Framework Core dead letter stores):

| Method | Route | Result |
|--------|-------|--------|
| `GET` | `{BasePath}/stats` | `200` with counts per status |
| `GET` | `{BasePath}/entries?count=50&skip=0` | `200` with pending entries, oldest first; `400` if `count` is not between 1 and 1000 or `skip < 0` |
| `GET` | `{BasePath}/entries/{id:guid}` | `200` with the entry, `404` if not found |
| `POST` | `{BasePath}/entries/{id:guid}/replay` | `204` after replaying the command, `404` if not found |
| `POST` | `{BasePath}/entries/{id:guid}/dismiss` | `204` after dismissing the entry, `404` if not found |

`CommandDeadLetterInspectorOptions` configures `BasePath` (default `/pulse/commands`) and `RouteGroupName` (default `Pulse Command Dead Letter Inspector`).

> No authorization is applied. Secure the returned route group yourself:

```csharp
app.MapCommandDeadLetterInspector(options => options.BasePath = "/admin/commands")
   .RequireAuthorization("Operations");
```

### Audit Inspector

`MapAuditInspector` maps read-only endpoints over the registered `IAuditManagement` (any Pulse audit store provider). There are no replay, dismiss or other mutating operations.

| Endpoint | Description |
| --- | --- |
| `GET {BasePath}/stats` | Success and failure counts (`AuditStatistics`) |
| `GET {BasePath}/entries` | Filtered, paginated audit records, most recent first |
| `GET {BasePath}/entries/{id:guid}` | A single audit record, or `404 Not Found` |

Query parameters for `GET {BasePath}/entries` (all optional, combined with AND):

| Parameter | Description |
| --- | --- |
| `commandType` | Exact request type name |
| `userId` | Exact user identifier |
| `from` / `to` | Inclusive `OccurredAt` bounds (ISO 8601); `from` must not be later than `to` |
| `result` | `Success` or `Failure` |
| `take` | Page size, between `1` and `1000` (default `50`) |
| `skip` | Number of records to skip, `0` or more (default `0`) |

Malformed or out-of-range values return `400 Bad Request`.

`AuditInspectorOptions` sets `BasePath` (default `/pulse/audit`) and `RouteGroupName` (default `Pulse Audit Inspector`, applied via `WithGroupName`). No authorization is built in, so secure the returned route group yourself:

```csharp
app.MapAuditInspector(options => options.BasePath = "/admin/audit")
   .RequireAuthorization("AuditReaders");
```

## Outbox Inspector

`MapOutboxInspector` maps a route group of administrative endpoints backed by `IOutboxManagement`. Use them to find stuck messages, replay dead letters and dismiss failures without direct database access. `IOutboxManagement` is registered by the outbox persistence provider, for example the Entity Framework, SQL Server, PostgreSQL, MySQL, SQLite, MongoDB or Cosmos DB package.

```csharp
app.MapOutboxInspector();                                   // default base path "/pulse/outbox"

app.MapOutboxInspector(options =>
{
    options.BasePath = "/admin/outbox";
    options.RouteGroupName = "Admin Outbox Inspector";
})
.RequireAuthorization("OutboxAdmin");                       // secure the whole group
```

| Method | Route | Description | Responses |
|---|---|---|---|
| `GET` | `{BasePath}/stats` | Message counts per status | `200` |
| `GET` | `{BasePath}/messages?pageSize=50&page=0&status=` | Messages in any status, newest update first; `status` is optional (`Pending`, `Processing`, `Completed`, `Failed`, `DeadLetter` or their numeric values). Read-only: messages are not locked or changed. | `200`, `400` |
| `GET` | `{BasePath}/messages/{id:guid}` | A single message in any status | `200`, `404` |
| `POST` | `{BasePath}/messages/{id:guid}/replay` | Alias of `dead-letters/{id}/replay`; only dead-letter messages can be replayed | `204`, `404` |
| `GET` | `{BasePath}/dead-letters?pageSize=50&page=0` | Dead-letter messages, newest update first | `200`, `400` |
| `GET` | `{BasePath}/dead-letters/count` | Number of dead-letter messages | `200` |
| `GET` | `{BasePath}/dead-letters/{id:guid}` | A single dead-letter message | `200`, `404` |
| `POST` | `{BasePath}/dead-letters/{id:guid}/replay` | Resets a dead-letter message to `Pending` | `204`, `404` |
| `POST` | `{BasePath}/dead-letters/{id:guid}/dismiss` | Permanently deletes a dead-letter message | `204`, `404` |
| `POST` | `{BasePath}/dead-letters/replay-all` | Resets all dead-letter messages to `Pending`; returns `{ "count": n }` | `200` |

`pageSize` must be at least `1` and `page` must not be negative. Invalid values and undefined `status` values return `400 Bad Request` with a validation problem body. Identifiers that are not GUIDs do not match any route and return `404 Not Found`.

### Options

| Property | Default | Description |
|---|---|---|
| `BasePath` | `/pulse/outbox` | Route prefix of the endpoint group |
| `RouteGroupName` | `Pulse Outbox Inspector` | Endpoint group name, for example for OpenAPI documents |

### Authorization

`MapOutboxInspector` applies **no authorization**. These endpoints expose message payloads and can replay or delete messages, so always secure the returned group, for example with `.RequireAuthorization()`, and never expose it publicly without protection.

> **Note for SQL Server and PostgreSQL:** the message listing, message lookup and dismiss operations use new stored procedures and functions. Re-run `Scripts/OutboxMessage.sql` of the provider package after upgrading. The script is idempotent.

## NativeAOT and Trimming

- `MapCommand`, `MapQuery`, `MapStreamQuery` and the inspector extensions (`MapOutboxInspector`, `MapAuditInspector`, `MapCommandDeadLetterInspector`) carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, because they build request delegates with `RequestDelegateFactory`. Trimmed and NativeAOT applications get a warning when they call them.
- The inspector endpoints write their responses with an internal source-generated `JsonSerializerContext` and the web defaults (camelCase). They ignore the application's `HttpJsonOptions`.
- `MapStreamQueryHub` and `PulseStreamHub<TQuery, TResponse>` build without trim or AOT warnings, but SignalR itself is not supported under NativeAOT on .NET 8 and only partially supported on .NET 9 and later. Under NativeAOT, register a source-generated `JsonSerializerContext` for `TQuery` and `TResponse` with the JSON hub protocol, for example `services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default))`, and observe the [SignalR NativeAOT restrictions](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-9.0#signalr).

See [NativeAOT and Trimming](https://github.com/dailydevops/pulse/blob/main/src/NetEvolve.Pulse/README.md#nativeaot-and-trimming) in the `NetEvolve.Pulse` README for the full list and the payload serialization setup.

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
