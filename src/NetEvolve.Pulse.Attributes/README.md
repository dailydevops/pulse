# NetEvolve.Pulse.Attributes

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.Attributes.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Attributes/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.Attributes.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Attributes/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.Attributes provides the `[PulseHandler]` marker attribute and the `PulseServiceLifetime` enum used to annotate handler classes for automatic DI registration code generation at compile time by the `NetEvolve.Pulse.SourceGeneration` source generator.

## Features

- **`[PulseHandler]` Attribute**: Marks handler classes for automatic DI registration code generation
- **`PulseServiceLifetime` Enum**: Configure the DI lifetime (Singleton, Scoped, Transient) per handler without depending on `Microsoft.Extensions.DependencyInjection.Abstractions`
- **Compile-Time Only**: The attribute is consumed at build time by the source generator and has no runtime overhead

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.Attributes
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.Attributes
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.Attributes" Version="x.x.x" />
```

## Quick Start

```csharp
using NetEvolve.Pulse.Attributes;
using NetEvolve.Pulse.Extensibility;

[PulseHandler]
public class CreateOrderCommandHandler
    : ICommandHandler<CreateOrderCommand, OrderResult>
{
    public Task<OrderResult> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(new OrderResult(Guid.NewGuid()));
}
```

## Usage

### Default Scoped Lifetime

```csharp
[PulseHandler] // defaults to Scoped
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, OrderResult>
{
    public Task<OrderResult> HandleAsync(
        CreateOrderCommand command, CancellationToken cancellationToken) => ...;
}
```

### Singleton Lifetime

```csharp
[PulseHandler(Lifetime = PulseServiceLifetime.Singleton)]
public class GetCachedDataHandler : IQueryHandler<GetCachedDataQuery, CachedData>
{
    public Task<CachedData> HandleAsync(
        GetCachedDataQuery query, CancellationToken cancellationToken) => ...;
}
```

### Transient Lifetime

```csharp
[PulseHandler(Lifetime = PulseServiceLifetime.Transient)]
public class TransientEventHandler : IEventHandler<MyEvent>
{
    public Task HandleAsync(MyEvent @event, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0

## Related Packages

- [**NetEvolve.Pulse.SourceGeneration**](https://www.nuget.org/packages/NetEvolve.Pulse.SourceGeneration/) - Roslyn source generator that consumes `[PulseHandler]` and emits DI registrations
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
