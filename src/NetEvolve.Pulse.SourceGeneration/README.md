# NetEvolve.Pulse.SourceGeneration

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.SourceGeneration.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SourceGeneration/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.SourceGeneration.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SourceGeneration/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.SourceGeneration is a Roslyn source generator for the Pulse CQRS mediator library. It automatically generates DI registration code for handler classes annotated with `[PulseHandler]`, eliminating manual service registrations and catching missing or duplicate registrations at compile time.

## Features

- **Compile-Time Code Generation**: Emits `IServiceCollection` extension methods with `TryAdd*` registrations for all annotated handlers
- **Incremental Generator**: Uses `ForAttributeWithMetadataName` for fast, IDE-friendly discovery
- **Configurable Lifetimes**: Supports `Singleton`, `Scoped` (default), and `Transient` via `PulseServiceLifetime` enum
- **Assembly-Derived Method Name**: Generated method name is derived from `AssemblyName` with dots removed (e.g., `NetEvolve.Pulse` → `AddNetEvolvePulseHandlers`)
- **Root Namespace Support**: Generated namespace uses the consuming project's `RootNamespace`
- **Diagnostics**: PULSE001 (error, no handler interface) and PULSE002 (warning, duplicate command/query handler)
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
services.AddMyProjectHandlers();
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

### Supported Handler Interfaces

| Interface | Description |
| --- | --- |
| `ICommandHandler<TCommand>` | Void command handler (single type parameter) |
| `ICommandHandler<TCommand, TResponse>` | Command handler with response |
| `IQueryHandler<TQuery, TResponse>` | Query handler |
| `IEventHandler<TEvent>` | Event handler (multiple handlers per event are valid) |

## Diagnostics

| Id | Severity | Description |
| --- | --- | --- |
| PULSE001 | Error | Type is annotated with `[PulseHandler]` but does not implement any known Pulse handler interface. |
| PULSE002 | Warning | Multiple `[PulseHandler]` types implement the same command or query handler contract. Events are excluded — multiple event handlers are valid. |

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
