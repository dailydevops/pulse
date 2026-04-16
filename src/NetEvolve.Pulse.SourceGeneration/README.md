# NetEvolve.Pulse.SourceGeneration

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.SourceGeneration.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SourceGeneration/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.SourceGeneration.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SourceGeneration/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.SourceGeneration is a Roslyn source generator for the Pulse CQRS mediator library. It automatically generates DI registration code for handler classes annotated with `[PulseHandler]`, `[PulseHandler<TMessage>]`, or `[PulseGenericHandler]`, eliminating manual service registrations and catching missing or duplicate registrations at compile time.

## Features

- **Compile-Time Code Generation**: Emits `IServiceCollection` extension methods with `TryAdd*` registrations for all annotated handlers
- **Closed Open-Generic Handler Support**: `[PulseHandler<TMessage>]` closes open-generic handler classes for specific message types at compile time; multiple attributes on the same class register it for multiple message types
- **Pure Open-Generic Handler Support**: `[PulseGenericHandler]` registers an open-generic handler class directly as an open-generic DI service (e.g. `services.TryAddScoped(typeof(ICommandHandler<,>), typeof(MyHandler<,>))`), allowing the DI container to resolve any closed variant at runtime
- **Incremental Generator**: Uses `ForAttributeWithMetadataName` for fast, IDE-friendly discovery
- **Configurable Lifetimes**: Supports `Singleton`, `Scoped` (default), and `Transient` via `PulseServiceLifetime` enum
- **Assembly-Derived Method Name**: Generated method name is derived from `AssemblyName` with dots removed and `PulseHandlers` appended (e.g., `MyProject` → `AddMyProjectPulseHandlers`)
- **Root Namespace Support**: Generated namespace uses the consuming project's `RootNamespace`
- **Multi-Interface Instance Sharing**: Handlers implementing multiple interfaces are registered as the concrete type once; each interface resolves via a factory delegate so all share the same instance within the configured lifetime
- **Diagnostics**: PULSE001–PULSE006 covering missing handler interfaces, duplicate registrations, open-generic type annotations, and invalid or incompatible explicit message type arguments
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

```csharp
using NetEvolve.Pulse.Attributes;
using NetEvolve.Pulse.Extensibility;

// 1. Annotate your handler classes
[PulseHandler]
public class CreateOrderHandler
    : ICommandHandler<CreateOrderCommand, OrderResult>
{
    public Task<OrderResult> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(new OrderResult(Guid.NewGuid()));
}

// 2. Call the generated extension method in your startup code
// Method name is derived from your assembly name
services.AddMyProjectPulseHandlers();
```

## Usage

### Handler Registration

Annotate handler classes with `[PulseHandler]` and the generator emits `TryAddScoped`, `TryAddSingleton`, or `TryAddTransient` calls based on the configured lifetime:

```csharp
[PulseHandler] // Scoped (default)
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, OrderResult> { ... }

[PulseHandler(Lifetime = PulseServiceLifetime.Singleton)]
public class GetCachedDataHandler : IQueryHandler<GetCachedDataQuery, CachedData> { ... }

[PulseHandler(Lifetime = PulseServiceLifetime.Transient)]
public class NotificationHandler : IEventHandler<OrderCreatedEvent> { ... }
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

// Generated: services.TryAddSingleton(
//     typeof(IEventHandler<>), typeof(GenericAuditEventHandler<>));
```

> **Note:** `[PulseHandler]` on an open-generic class produces a **PULSE004** error — use `[PulseGenericHandler]` instead when you need a true open-generic DI registration.

### Supported Handler Interfaces

| Interface | Description |
| --- | --- |
| `ICommandHandler<TCommand>` | Void command handler (single type parameter) |
| `ICommandHandler<TCommand, TResponse>` | Command handler with response |
| `IQueryHandler<TQuery, TResponse>` | Query handler |
| `IEventHandler<TEvent>` | Event handler (multiple handlers per event are valid) |
| `IStreamQueryHandler<TQuery, TResponse>` | Streaming query handler |

## Diagnostics

| Id | Severity | Description |
| --- | --- | --- |
| PULSE001 | Error | Type is annotated with `[PulseHandler]` but does not implement any known Pulse handler interface. |
| PULSE002 | Warning | Multiple `[PulseHandler]` types implement the same command or query handler contract. Events are excluded — multiple event handlers are valid. |
| PULSE004 | Error | Type annotated with `[PulseHandler]` is an open generic type and cannot be automatically registered. Use `[PulseHandler<TMessage>]` for closed registrations or `[PulseGenericHandler]` for open-generic DI registrations. |
| PULSE005 | Error | The type argument `T` passed to `[PulseHandler<T>]` does not implement any known Pulse message interface (`ICommand`, `ICommand<T>`, `IQuery<T>`, `IEvent`, or `IStreamQuery<T>`). |
| PULSE006 | Error | A closed registration for the given message type cannot be constructed because the handler does not implement a compatible handler interface or not all type parameters can be inferred from the message type. |

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- `NetEvolve.Pulse.Attributes` package for the `[PulseHandler]` attribute
- `NetEvolve.Pulse.Extensibility` package for handler interfaces

## Related Packages

- [**NetEvolve.Pulse.Attributes**](https://www.nuget.org/packages/NetEvolve.Pulse.Attributes/) - `[PulseHandler]` attribute and `PulseServiceLifetime` enum
- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core CQRS mediator
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Handler and interceptor contracts

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
