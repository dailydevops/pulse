# NetEvolve.Pulse.Extensibility

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.Extensibility.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.Extensibility.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.Extensibility delivers the core contracts for building CQRS mediators: commands, queries, events, handlers, interceptors, and configurators that compose the Pulse pipeline.

## Features

* Minimal abstractions for commands, queries, events, and request/response flows
* Strongly typed handler interfaces with single-handler guarantees for commands and queries
* Interceptor interfaces for cross-cutting concerns (logging, validation, metrics, caching)
* Fluent mediator configuration via `IMediatorConfigurator` and extension methods
* Designed for framework-agnostic use while pairing seamlessly with NetEvolve.Pulse
* Test-friendly primitives including `Void` responses and TimeProvider awareness

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.Extensibility
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.Extensibility
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.Extensibility" Version="x.x.x" />
```

## Quick Start

```csharp
using NetEvolve.Pulse.Extensibility;

public record CreateInvoiceCommand(string CustomerId, decimal Amount) : ICommand<InvoiceCreated>;
public record InvoiceCreated(Guid InvoiceId);

public sealed class CreateInvoiceHandler
    : ICommandHandler<CreateInvoiceCommand, InvoiceCreated>
{
    public Task<InvoiceCreated> HandleAsync(
        CreateInvoiceCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(new InvoiceCreated(Guid.NewGuid()));
}

public record GetInvoiceQuery(Guid Id) : IQuery<Invoice>;
public record Invoice(Guid Id, string CustomerId, decimal Amount);

public sealed class GetInvoiceHandler : IQueryHandler<GetInvoiceQuery, Invoice>
{
    public Task<Invoice> HandleAsync(GetInvoiceQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new Invoice(query.Id, "CUST-123", 125.00m));
}
```

## Usage

### Basic Example

Pair the contracts with the Pulse mediator for DI and dispatching:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

var services = new ServiceCollection();
services.AddPulse();
services.AddScoped<ICommandHandler<CreateInvoiceCommand, InvoiceCreated>, CreateInvoiceHandler>();
services.AddScoped<IQueryHandler<GetInvoiceQuery, Invoice>, GetInvoiceHandler>();

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

var created = await mediator.SendAsync<CreateInvoiceCommand, InvoiceCreated>(
    new CreateInvoiceCommand("CUST-123", 125.00m));
var invoice = await mediator.QueryAsync<GetInvoiceQuery, Invoice>(
    new GetInvoiceQuery(created.InvoiceId));
```

### Advanced Example

Extend the configurator with your own interceptors and plug them into Pulse:

```csharp
using NetEvolve.Pulse.Extensibility;

public static class MediatorConfiguratorExtensions
{
    public static IMediatorConfigurator AddCustomValidation(
        this IMediatorConfigurator configurator)
    {
        // Register validation interceptors or pipelines here
        return configurator;
    }
}

// Register with Pulse
services.AddPulse(config =>
{
    config.AddActivityAndMetrics()
          .AddCustomValidation();
});
```

## Configuration

```csharp
// Configure mediator features during startup
services.AddPulse(config =>
{
    // Built-in observability interceptors
    config.AddActivityAndMetrics();

    // Custom extension methods for validation, caching, retries, etc.
    // config.AddCustomValidation();
});
```

## Requirements

* .NET 8.0, .NET 9.0, or .NET 10.0
* Suitable for ASP.NET Core, console, worker, and library projects
* OpenTelemetry packages required when using `AddActivityAndMetrics()` through Pulse

## Related Packages

* [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Mediator implementation built on these abstractions

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
