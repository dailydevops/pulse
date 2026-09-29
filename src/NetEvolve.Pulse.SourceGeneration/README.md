# NetEvolve.Pulse.SourceGeneration

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.SourceGeneration.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SourceGeneration/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.SourceGeneration.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SourceGeneration/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.SourceGeneration is a Roslyn source generator for the Pulse CQRS mediator library. It automatically generates DI registration code for handler classes annotated with `[PulseHandler]`, `[PulseHandler<TMessage>]`, or `[PulseGenericHandler]`, eliminating manual service registrations and catching missing or duplicate registrations at compile time.

## Features

- **Compile-Time Code Generation**: Emits `IServiceCollection` extension methods with `TryAdd*` registrations for command, query and stream query handlers and `TryAddEnumerable` registrations for event handlers
- **Closed Open-Generic Handler Support**: `[PulseHandler<TMessage>]` closes open-generic handler classes for specific message types at compile time; multiple attributes on the same class register it for multiple message types
- **Pure Open-Generic Handler Support**: `[PulseGenericHandler]` registers an open-generic handler class directly as an open-generic DI service (e.g. `services.TryAddScoped(typeof(ICommandHandler<,>), typeof(MyHandler<,>))`), allowing the DI container to resolve any closed variant at runtime
- **Incremental Generator**: Uses `ForAttributeWithMetadataName` for fast, IDE-friendly discovery
- **Configurable Lifetimes**: Supports `Singleton`, `Scoped` (default), and `Transient` via `PulseServiceLifetime` enum
- **Assembly-Derived Method Name**: Generated method name is `Add` + `AssemblyName` + `PulseHandlers`. Dots are removed and every other character that is not valid in a C# identifier (for example `-`, space or `+`) is replaced with `_` (e.g., `MyProject` → `AddMyProjectPulseHandlers`, `My.Project` → `AddMyProjectPulseHandlers`, `my-service` → `Addmy_servicePulseHandlers`)
- **Root Namespace Support**: Generated namespace uses the consuming project's `RootNamespace`
- **Multi-Interface Instance Sharing**: Handlers implementing multiple interfaces are registered as the concrete type once; each interface resolves via a factory delegate so all share the same instance within the configured lifetime
- **Diagnostics**: PULSE001–PULSE007 covering missing handler interfaces, duplicate registrations, hints for handlers without `[PulseHandler]`, open-generic type annotations, invalid or incompatible explicit message type arguments, and handler types that generated code cannot reference or DI cannot instantiate
- **Fully Qualified Names**: All generated code uses `global::` prefixed type names to avoid namespace conflicts

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.SourceGeneration
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.SourceGeneration
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.SourceGeneration" Version="x.x.x" />
```

## Quick Start

The attributes and the handler interfaces ship in `NetEvolve.Pulse.Extensibility`. The examples assume a project named `MyProject` with implicit usings enabled.

```csharp
namespace MyProject;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

public sealed record CreateOrderCommand(string CustomerId) : ICommand<OrderResult>
{
    public string? CausationId { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed record OrderResult(Guid OrderId);

// 1. Annotate your handler classes
[PulseHandler]
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, OrderResult>
{
    public Task<OrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new OrderResult(Guid.NewGuid()));
}

public static class OrderingServiceCollectionExtensions
{
    // 2. Call the generated extension method in your startup code.
    // The method name is derived from the assembly name, and the method lives in the root namespace.
    public static IServiceCollection AddOrdering(this IServiceCollection services) =>
        services.AddMyProjectPulseHandlers();
}
```

The following examples omit the `namespace` and `using` lines and the message types. Declare the message types like `CreateOrderCommand` above.

## Usage

### Handler Registration

Annotate handler classes with `[PulseHandler]` and the generator emits `TryAddScoped`, `TryAddSingleton`, or `TryAddTransient` calls based on the configured lifetime. Command, query and stream query handlers have exactly one handler per message type, so an existing registration wins.

An event can have several handlers. For `IEventHandler<TEvent>` the generator emits `TryAddEnumerable` with a `ServiceDescriptor` of the configured lifetime, for example `services.TryAddEnumerable(ServiceDescriptor.Transient<IEventHandler<OrderCreatedEvent>, NotificationHandler>())`. Every annotated event handler is registered next to handlers from `AddEventHandler` and `AddOutbox`, and calling the generated method twice does not register a handler twice.

```csharp
[PulseHandler] // Scoped (default)
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, OrderResult>
{
    public Task<OrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new OrderResult(Guid.NewGuid()));
}

[PulseHandler(Lifetime = PulseServiceLifetime.Singleton)]
public class GetCachedDataHandler : IQueryHandler<GetCachedDataQuery, CachedData>
{
    public Task<CachedData> HandleAsync(GetCachedDataQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CachedData(query.Key));
}

[PulseHandler(Lifetime = PulseServiceLifetime.Transient)]
public class NotificationHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
```

A command without a result implements `ICommand`, which is `ICommand<Void>`. Its handler implements `ICommandHandler<TCommand, Void>`:

```csharp
[PulseHandler]
public class ShipOrderHandler : ICommandHandler<ShipOrderCommand, Void>
{
    public Task<Void> HandleAsync(ShipOrderCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(Void.Completed);
}
```

### Closed Open-Generic Handler Registration

Use `[PulseHandler<TMessage>]` to close an open-generic handler class for a specific message type. Apply the attribute multiple times to register the same class for several message types:

```csharp
// Register the generic handler for two concrete command types
[PulseHandler<CreateOrderCommand>]
[PulseHandler<CancelOrderCommand>]
public class GenericCommandHandler<TCmd, TResult> : ICommandHandler<TCmd, TResult>
    where TCmd : ICommand<TResult>
{
    public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken) =>
        Task.FromResult(default(TResult)!);
}

// Register with a non-default lifetime
[PulseHandler<OrderShippedEvent>(Lifetime = PulseServiceLifetime.Singleton)]
public class GenericAuditEventHandler<TEvent> : IEventHandler<TEvent>
    where TEvent : IEvent
{
    public Task HandleAsync(TEvent message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

The same works for a concrete class that implements one handler interface per message type. Each attribute registers the interface whose message argument is the attribute's type argument:

```csharp
[PulseHandler<CreateOrderCommand>]
[PulseHandler<CancelOrderCommand>]
public sealed class OrderCommandHandler
    : ICommandHandler<CreateOrderCommand, OrderResult>,
        ICommandHandler<CancelOrderCommand, OrderResult>
{
    public Task<OrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new OrderResult(Guid.NewGuid()));

    public Task<OrderResult> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(new OrderResult(command.OrderId));
}
```

### Pure Open-Generic Handler Registration

Use `[PulseGenericHandler]` when you want a single open-generic class to handle _any_ closed variant of a message type, resolved by the DI container at runtime. The generator emits a `typeof()`-based registration instead of a closed-type one:

```csharp
// Handles ICommandHandler<TCommand, TResult> for any TCommand : ICommand<TResult>
[PulseGenericHandler]
public class GenericCommandHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(default(TResult)!);
}

// Generated: services.TryAddScoped(
//     typeof(ICommandHandler<,>), typeof(GenericCommandHandler<,>));

// Handles IEventHandler<TEvent> for any TEvent : IEvent, registered as Singleton
[PulseGenericHandler(Lifetime = PulseServiceLifetime.Singleton)]
public class GenericAuditEventHandler<TEvent> : IEventHandler<TEvent>
    where TEvent : IEvent
{
    public Task HandleAsync(TEvent message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

// Generated: services.TryAddEnumerable(ServiceDescriptor.Singleton(
//     typeof(IEventHandler<>), typeof(GenericAuditEventHandler<>)));
```

> **Note:** `[PulseHandler]` on an open-generic class produces a **PULSE004** error — use `[PulseGenericHandler]` instead when you need a true open-generic DI registration.

### Supported Handler Interfaces

| Interface | Description |
| --- | --- |
| `ICommandHandler<TCommand, TResponse>` | Command handler with response, registered with `TryAdd*` (one handler per command) |
| `ICommandHandler<TCommand, Void>` | Void command handler for `ICommand` (which is `ICommand<Void>`), registered with `TryAdd*` |
| `IQueryHandler<TQuery, TResponse>` | Query handler, registered with `TryAdd*` (one handler per query) |
| `IStreamQueryHandler<TQuery, TResponse>` | Streaming query handler, registered with `TryAdd*` (one handler per stream query) |
| `IEventHandler<TEvent>` | Event handler, registered with `TryAddEnumerable` (multiple handlers per event are valid, all of them are registered) |

## Diagnostics

| Id | Severity | Description |
| --- | --- | --- |
| PULSE001 | Error | Type is annotated with `[PulseHandler]` but does not implement any known Pulse handler interface. |
| PULSE002 | Warning | Multiple `[PulseHandler]` types implement the same command, query or stream query handler contract. Only the first handler is registered. Events are excluded — multiple event handlers are valid. |
| PULSE003 | Info | Type implements a Pulse handler interface but is not annotated with `[PulseHandler]` or `[PulseHandler<TMessage>]`, so it is not registered. Not reported for types that could not be registered anyway (open generic, nested in a generic type, or a PULSE007 case). |
| PULSE004 | Error | The annotated type is an open generic type or is nested in a generic type and cannot be automatically registered. For an open generic type annotated with `[PulseHandler]`, use `[PulseHandler<TMessage>]` for closed registrations or `[PulseGenericHandler]` for open-generic DI registrations. For a handler nested in a generic type (reported for all three attributes, because the generated code cannot name the containing type's type arguments), move the handler out of the generic containing type. |
| PULSE005 | Error | The type argument `T` passed to `[PulseHandler<T>]` does not implement any known Pulse message interface (`ICommand`, `ICommand<T>`, `IQuery<T>`, `IEvent`, or `IStreamQuery<T>`). |
| PULSE006 | Error | A closed registration for the given message type cannot be constructed because the handler does not implement a compatible handler interface or not all type parameters can be inferred from the message type, or the inferred type arguments do not satisfy the handler's generic constraints. |
| PULSE007 | Error | The annotated type cannot be registered: the type, a containing type, or a message or response type (including its type arguments) of a handler interface or of `[PulseHandler<TMessage>]` is `private`, `protected`, `private protected` or `file`-local (generated code cannot reference it), or the type is `abstract`, `static` or a value type (the DI container cannot instantiate it). Make the type a concrete class, and make it and the types it handles `internal`, `protected internal` or `public` along their whole containing chain. |

## NativeAOT and Trimming

The generated registration method only emits generic `TryAdd*<TService, TImplementation>()` and `ServiceDescriptor.{Lifetime}<TService, TImplementation>()` calls and `typeof(...)` literals for open-generic handlers. Both satisfy the `[DynamicallyAccessedMembers(PublicConstructors)]` annotations of `Microsoft.Extensions.DependencyInjection`, so the trimmer keeps every registered handler and its constructor without an `ILLink.Descriptors.xml` file or `[DynamicDependency]` attributes. The `samples/NetEvolve.Pulse.Xample.Aot` smoke application verifies this with a NativeAOT publish on every pull request.

Open-generic handlers registered with `[PulseGenericHandler]` are closed by the DI container at runtime. Under NativeAOT this only works for reference-type type arguments.

### Interceptors for Value-Type Requests

The DI container also cannot close the open-generic interceptors of `NetEvolve.Pulse` over value types under NativeAOT. When the project references `NetEvolve.Pulse`, the generated method therefore ends with one `NativeAotInterceptorExtensions` call per registered command, query or stream query handler whose request or response type is a value type, including `Void`:

```csharp
global::NetEvolve.Pulse.NativeAotInterceptorExtensions.AddNativeAotCommandInterceptors<global::MyProject.AddNumbersCommand, int>(services);
global::NetEvolve.Pulse.NativeAotInterceptorExtensions.AddNativeAotCommandInterceptors<global::MyProject.PingCommand, global::NetEvolve.Pulse.Extensibility.Void>(services);
```

Under NativeAOT, each call registers closed variants of the built-in interceptors that are registered at that time, so call the generated method after `AddPulse(...)` and after every other interceptor registration, such as closed application interceptors or Polly policies added by a later `AddPulse(...)` call. When an interceptor for such a request is registered, replaced or removed afterwards, the mediator throws an `InvalidOperationException` for that request instead of skipping the interceptor. Exclusive commands, queries and stream queries use the matching `AddNativeAotExclusiveCommandInterceptors`, `AddNativeAotQueryInterceptors` and `AddNativeAotStreamQueryInterceptors` methods. Projects that only reference `NetEvolve.Pulse.Extensibility`, and handlers with reference-type requests and responses, generate no calls. See "Value-Type Requests Under NativeAOT" in the `NetEvolve.Pulse` README for the covered interceptors.

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- `NetEvolve.Pulse.Extensibility` package for the handler interfaces and the `[PulseHandler]`, `[PulseHandler<TMessage>]` and `[PulseGenericHandler]` attributes (namespace `NetEvolve.Pulse.Extensibility.Attributes`)

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core CQRS mediator
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Handler and interceptor contracts, `[PulseHandler]` attributes and `PulseServiceLifetime` enum

## Documentation

For complete documentation, please visit the [official documentation](https://github.com/dailydevops/pulse/blob/main/README.md).

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
