# NetEvolve.Pulse.AspNetCore.Grpc

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.AspNetCore.Grpc.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AspNetCore.Grpc/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.AspNetCore.Grpc.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AspNetCore.Grpc/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.AspNetCore.Grpc exposes Pulse streaming queries (`IStreamQuery<TResponse>`) as gRPC server-streaming RPCs. It is a separate package, so the core `NetEvolve.Pulse.AspNetCore` integration does not depend on gRPC.

## Features

- **`PulseGrpcStreamService<TQuery, TResponse>`**: a base class that runs `IMediator.StreamQueryAsync` and writes every item, in order, to the `IServerStreamWriter<TResponse>`.
- **Message mapping**: an overload of `StreamAsync` takes a `Func<TResponse, TMessage>`, so handlers yield domain types and the service maps them to Protobuf messages.
- **Client cancellation**: `ServerCallContext.CancellationToken` is passed to the mediator and checked before every write. The stream stops even when the handler ignores the token, as soon as the handler yields its next item.
- **Error propagation**: exceptions from the handler or interceptors propagate to ASP.NET Core gRPC, which translates them into a gRPC status.
- **`MapStreamQueryGrpc<TService>()`**: registers the service on any `IEndpointRouteBuilder`.

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.AspNetCore.Grpc
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.AspNetCore.Grpc
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.AspNetCore.Grpc" Version="x.x.x" />
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddPulse(c => c.AddStreamQueryHandler<OrdersStreamQuery, OrderReply, OrdersStreamQueryHandler>());

var app = builder.Build();

app.MapStreamQueryGrpc<OrderStreamService>(); // anonymous; see Authorization below

app.Run();
```

## Usage

### Implementing a streaming service

ASP.NET Core gRPC binds a service through the `[BindServiceMethod]` attribute. It then calls the `public virtual` method with the same name as the RPC, declared on the type that the attribute names. The service class must therefore not be `sealed`. C# allows only one base class, so a `PulseGrpcStreamService` subclass cannot also derive from the `Orders.OrdersBase` class that Grpc.Tools generates. Add a small static bridge to the generated `BindService` method instead:

```protobuf
service Orders {
  rpc StreamOrders (OrdersRequest) returns (stream OrderReply);
}
```

```csharp
using Grpc.Core;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

public sealed record OrdersStreamQuery(string CustomerId) : IStreamQuery<OrderReply>
{
    public string? CausationId { get; set; }
    public string? CorrelationId { get; set; }
}

[BindServiceMethod(typeof(OrderStreamService), nameof(BindService))]
public class OrderStreamService(IMediator mediator)
    : PulseGrpcStreamService<OrdersStreamQuery, OrderReply>(mediator)
{
    // Reuses the generated method descriptors; the handler is resolved by name (StreamOrders).
    public static void BindService(ServiceBinderBase binder, OrderStreamService service) =>
        Orders.BindService(binder, null);

    public virtual Task StreamOrders(
        OrdersRequest request,
        IServerStreamWriter<OrderReply> responseStream,
        ServerCallContext context
    ) => StreamAsync(new OrdersStreamQuery(request.CustomerId), responseStream, context);
}
```

The stream item type (`TResponse`) is written to the gRPC stream as it is. To keep handlers free of generated Protobuf types, let the query yield a domain type and pass a mapping function to the `StreamAsync` overload:

```csharp
public sealed record OrdersStreamQuery(string CustomerId) : IStreamQuery<Order> { /* ... */ }

[BindServiceMethod(typeof(OrderStreamService), nameof(BindService))]
public class OrderStreamService(IMediator mediator)
    : PulseGrpcStreamService<OrdersStreamQuery, Order>(mediator)
{
    public static void BindService(ServiceBinderBase binder, OrderStreamService service) =>
        Orders.BindService(binder, null);

    public virtual Task StreamOrders(
        OrdersRequest request,
        IServerStreamWriter<OrderReply> responseStream,
        ServerCallContext context
    ) => StreamAsync(
        new OrdersStreamQuery(request.CustomerId),
        responseStream,
        context,
        static order => new OrderReply { Id = order.Id, Total = order.Total }
    );
}
```

### Authorization

`MapStreamQueryGrpc<TService>()` maps the service **without** authentication or authorization, so any caller can open the stream. Secure it like any other endpoint, either on the mapping or with `[Authorize]` on the service class:

```csharp
builder.Services.AddAuthorization();

app.UseAuthentication();
app.UseAuthorization();

app.MapStreamQueryGrpc<OrderStreamService>().RequireAuthorization("ReadOrders");
```

### Cancellation and errors

`StreamAsync` passes `context.CancellationToken` to the mediator and checks it before each write. When the client cancels, an `OperationCanceledException` ends the call. Exceptions from handlers are not caught. ASP.NET Core gRPC turns them into an error status (`RpcException` keeps its status).

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- ASP.NET Core (`Microsoft.AspNetCore.App` framework reference)
- `Grpc.AspNetCore.Server` (added as a dependency)
- `services.AddGrpc()` must be called before mapping services

## Contributing

Contributions are welcome! Please read the [Contributing Guidelines](https://github.com/dailydevops/pulse/blob/main/CONTRIBUTING.md) before submitting a pull request.

## Support

- **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/dailydevops/pulse/issues)
- **Documentation**: Read the full documentation at [https://github.com/dailydevops/pulse](https://github.com/dailydevops/pulse)

## License

This project is licensed under the MIT License — see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core CQRS mediator
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Extensibility contracts
- [**NetEvolve.Pulse.AspNetCore**](https://www.nuget.org/packages/NetEvolve.Pulse.AspNetCore/) - ASP.NET Core Minimal API integration, including `MapStreamQuery` for SSE and NDJSON

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
