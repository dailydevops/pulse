# NetEvolve.Pulse.HttpCorrelation

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.HttpCorrelation.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.HttpCorrelation/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.HttpCorrelation.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.HttpCorrelation/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.HttpCorrelation automatically propagates the HTTP correlation ID from `IHttpCorrelationAccessor` into every `IRequest<TResponse>` and `IEvent` dispatched through the Pulse mediator — eliminating the need to manually copy the correlation ID at every call site.

## Features

- **Zero-effort correlation**: Automatically enriches every mediator request and event with the current HTTP correlation ID.
- **Non-destructive**: Never overwrites a `CorrelationId` that has already been set by the caller.
- **Optional dependency**: When `IHttpCorrelationAccessor` is not registered (e.g. background services), both interceptors pass through silently without error.
- **Idempotent registration**: Calling `AddHttpCorrelationEnrichment()` multiple times registers each interceptor only once.
- **Scoped lifetime**: Interceptors are scoped so they resolve the current request's `IHttpCorrelationAccessor` instance on every invocation — no captive dependency issues.

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.HttpCorrelation
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.HttpCorrelation
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.HttpCorrelation" Version="x.x.x" />
```

## Quick Start

Register both interceptors in one call at startup:

```csharp
services.AddPulse(c => c.AddHttpCorrelationEnrichment());
```

> **Prerequisites**: You must also register `IHttpCorrelationAccessor` using your chosen `NetEvolve.Http.Correlation.*` middleware package (e.g. `NetEvolve.Http.Correlation.AspNetCore`).

## Usage

### ASP.NET Core Example

```csharp
using NetEvolve.Http.Correlation.AspNetCore;
using NetEvolve.Pulse;

var builder = WebApplication.CreateBuilder(args);

// 1. Register the HTTP correlation middleware (provides IHttpCorrelationAccessor)
builder.Services.AddHttpCorrelation();

// 2. Register the Pulse mediator with HTTP correlation enrichment
builder.Services.AddPulse(c => c.AddHttpCorrelationEnrichment());

var app = builder.Build();

// 3. Add the correlation middleware to the pipeline
app.UseHttpCorrelation();
app.Run();
```

From this point on, any `IRequest<TResponse>` or `IEvent` whose `CorrelationId` is `null` or empty will automatically receive the value from the `X-Correlation-ID` header of the current HTTP request.

### Propagation Logic

The interceptors apply the following rule before invoking the next handler:

```
if request.CorrelationId is null or empty
    → resolve IHttpCorrelationAccessor
    → if accessor.CorrelationId is non-empty
        → request.CorrelationId = accessor.CorrelationId
```

The accessor is resolved **lazily** — only when the message actually needs a correlation ID. Existing non-empty `CorrelationId` values are **never overwritten**, so callers can explicitly set a custom correlation ID that will be respected throughout the pipeline.

### Background Services

When dispatching mediator messages outside of an HTTP context (e.g. from a hosted service or message consumer), `IHttpCorrelationAccessor` may not be registered. In that case both interceptors pass through without any modification or error:

```csharp
// No IHttpCorrelationAccessor registered — interceptors are no-ops
services.AddPulse(c => c.AddHttpCorrelationEnrichment());
```

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- `Microsoft.Extensions.DependencyInjection.Abstractions` for `IServiceProvider` extensions
- `NetEvolve.Http.Correlation.Abstractions` for `IHttpCorrelationAccessor`
- A compatible `NetEvolve.Http.Correlation.*` implementation package to register `IHttpCorrelationAccessor`

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
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Extensibility contracts
- [**NetEvolve.Pulse.EntityFramework**](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework Core outbox persistence
- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) - SQL Server ADO.NET outbox persistence
- [**NetEvolve.Http.Correlation.AspNetCore**](https://www.nuget.org/packages/NetEvolve.Http.Correlation.AspNetCore/) - ASP.NET Core middleware that provides `IHttpCorrelationAccessor`

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
