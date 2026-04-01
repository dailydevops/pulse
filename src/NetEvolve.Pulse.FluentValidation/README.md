# NetEvolve.Pulse.FluentValidation

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.FluentValidation.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.FluentValidation/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.FluentValidation.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.FluentValidation/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.FluentValidation automatically validates commands and queries before they reach their handlers using FluentValidation validators registered in the DI container — centralizing validation at the pipeline boundary without duplicating logic inside individual handlers.

## Features

- **Automatic validation**: Resolves all `IValidator<TRequest>` instances and executes them before the handler runs.
- **Failure aggregation**: Collects failures from all validators and throws a single `ValidationException` if any exist.
- **Pass-through when no validators**: Requests with no registered validators flow to the handler unchanged — no errors, no configuration needed.
- **Multiple validators supported**: All registered validators for a given request type are executed and their failures are merged.
- **Idempotent registration**: Calling `AddFluentValidation()` multiple times registers the interceptor only once.
- **Scoped lifetime**: The interceptor resolves validators from the current DI scope on every invocation.

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.FluentValidation
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.FluentValidation
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.FluentValidation" Version="x.x.x" />
```

## Quick Start

Register the interceptor at startup:

```csharp
services.AddPulse(c => c.AddFluentValidation());
```

Register your FluentValidation validators:

```csharp
// Register individually
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();

// Or use FluentValidation's assembly scanning
services.AddValidatorsFromAssembly(typeof(CreateOrderCommand).Assembly);
```

## Usage

### Command Validation

```csharp
using FluentValidation;
using NetEvolve.Pulse.Extensibility;

public record CreateOrderCommand(string ProductId, int Quantity) : ICommand<OrderResult>
{
    public string? CorrelationId { get; set; }
}

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("ProductId must not be empty.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
    }
}
```

When `CreateOrderCommand` is dispatched with an empty `ProductId` or a non-positive `Quantity`, a `ValidationException` is thrown before the handler executes.

### Query Validation

```csharp
public record GetOrderQuery(string OrderId) : IQuery<OrderResult>
{
    public string? CorrelationId { get; set; }
}

public class GetOrderQueryValidator : AbstractValidator<GetOrderQuery>
{
    public GetOrderQueryValidator() =>
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId must not be empty.");
}
```

### Multiple Validators

When multiple `IValidator<TRequest>` implementations are registered for the same request type, all are executed and their failures are aggregated into one `ValidationException`:

```csharp
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderBusinessRulesValidator>();
```

## Behavior

| Scenario | Behavior |
|---|---|
| No validators registered | Request passes through to the handler unchanged |
| Validators registered, valid input | Request passes through to the handler |
| Validators registered, invalid input | `ValidationException` thrown before the handler executes |
| Multiple validators, one or more fail | All validators run; all failures are aggregated into one `ValidationException` |

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- `FluentValidation` 12.0 or later
- `NetEvolve.Pulse.Extensibility` for `IRequestInterceptor<,>` and `IMediatorBuilder`

## Contributing

Contributions are welcome! Please read the [Contributing Guidelines](https://github.com/dailydevops/pulse/blob/main/CONTRIBUTING.md) before submitting a pull request.

## Support

- **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/dailydevops/pulse/issues)
- **Documentation**: Read the full documentation at [https://github.com/dailydevops/pulse](https://github.com/dailydevops/pulse)

## License

This project is licensed under the MIT License — see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core CQRS mediator
- [**NetEvolve.Pulse.Polly**](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/) - Polly v8 resilience interceptors
- [**NetEvolve.Pulse.HttpCorrelation**](https://www.nuget.org/packages/NetEvolve.Pulse.HttpCorrelation/) - HTTP correlation ID propagation
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Extensibility contracts
- [**NetEvolve.Pulse.EntityFramework**](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework Core outbox persistence
- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) - SQL Server ADO.NET outbox persistence

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
