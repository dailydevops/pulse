# NetEvolve Pulse

[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](LICENSE)
[![Build Status](https://img.shields.io/github/actions/workflow/status/dailydevops/pulse/ci.yml?branch=main)](https://github.com/dailydevops/pulse/actions)
[![Contributors](https://img.shields.io/github/contributors/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/graphs/contributors)

## Overview

NetEvolve Pulse delivers a high-performance CQRS mediator with an interceptor-enabled pipeline for commands, queries, and events. It targets .NET services that need predictable dispatching, strong typing, and first-class observability. The solution is designed for APIs, background workers, and modular libraries that require clean message handling with minimal ceremony.

## Projects

### Core Libraries

- **NetEvolve.Pulse** — Mediator implementation with interceptor pipeline, outbox pattern support, and DI integration ([src/NetEvolve.Pulse/README.md](src/NetEvolve.Pulse/README.md))
- **NetEvolve.Pulse.Extensibility** — Contracts and abstractions for commands, queries, events, handlers, and configurators ([src/NetEvolve.Pulse.Extensibility/README.md](src/NetEvolve.Pulse.Extensibility/README.md))

### Provider Libraries

- **NetEvolve.Pulse.EntityFramework** — Entity Framework Core persistence for the outbox pattern ([src/NetEvolve.Pulse.EntityFramework/README.md](src/NetEvolve.Pulse.EntityFramework/README.md))
- **NetEvolve.Pulse.SqlServer** — SQL Server ADO.NET persistence for the outbox pattern ([src/NetEvolve.Pulse.SqlServer/README.md](src/NetEvolve.Pulse.SqlServer/README.md))
- **NetEvolve.Pulse.Polly** — Polly v8 resilience policies integration for retry, circuit breaker, and timeout strategies ([src/NetEvolve.Pulse.Polly/README.md](src/NetEvolve.Pulse.Polly/README.md))

### Tests

- **NetEvolve.Pulse.Tests.Unit** — Unit coverage for mediator behaviors ([tests/NetEvolve.Pulse.Tests.Unit](tests/NetEvolve.Pulse.Tests.Unit))
- **NetEvolve.Pulse.Tests.Integration** — Integration scenarios and pipeline validation ([tests/NetEvolve.Pulse.Tests.Integration](tests/NetEvolve.Pulse.Tests.Integration))
- **NetEvolve.Pulse.EntityFramework.Tests.Integration** — Entity Framework outbox integration tests ([tests/NetEvolve.Pulse.EntityFramework.Tests.Integration](tests/NetEvolve.Pulse.EntityFramework.Tests.Integration))
- **NetEvolve.Pulse.SqlServer.Tests.Integration** — SQL Server outbox integration tests ([tests/NetEvolve.Pulse.SqlServer.Tests.Integration](tests/NetEvolve.Pulse.SqlServer.Tests.Integration))
- **NetEvolve.Pulse.Polly.Tests.Unit** — Polly integration unit tests ([tests/NetEvolve.Pulse.Polly.Tests.Unit](tests/NetEvolve.Pulse.Polly.Tests.Unit))
- **NetEvolve.Pulse.Polly.Tests.Integration** — Polly integration tests ([tests/NetEvolve.Pulse.Polly.Tests.Integration](tests/NetEvolve.Pulse.Polly.Tests.Integration))

## Features

- Typed CQRS mediator with single-handler enforcement for commands and queries, plus fan-out event dispatch
- Interceptor pipeline for logging, metrics, tracing, validation, retries, and other cross-cutting concerns via `IMediatorConfigurator`
- **Outbox pattern** with background processor for reliable event delivery via `AddOutbox()`
- OpenTelemetry-friendly hooks through `AddActivityAndMetrics()` and TimeProvider-aware flows for deterministic testing and scheduling
- Minimal DI setup with `services.AddPulse(...)`, scoped lifetimes, and opt-in configurators per application
- Contracts in `NetEvolve.Pulse.Extensibility` for framework-agnostic use or deep integration with ASP.NET Core
- Parallel event dispatch with cancellation support to keep handlers responsive under load
- Built-in primitives like `Void` to simplify command semantics without return values
- **Multiple persistence providers**: Entity Framework Core (provider-agnostic) and SQL Server ADO.NET
- **Polly v8 integration** for retry, circuit breaker, timeout, bulkhead, and fallback strategies

## Getting Started

### Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or higher (solution also targets .NET 8 and .NET 9)
- [Git](https://git-scm.com/) for source control
- [Visual Studio Code](https://code.visualstudio.com/) or [Visual Studio 2022](https://visualstudio.microsoft.com/) for development

### Installation

1. Clone the repository:

   ```bash
   git clone https://github.com/dailydevops/pulse.git
   cd pulse
   ```

2. Restore dependencies:

   ```bash
   dotnet restore
   ```

3. Build the solution:

   ```bash
   dotnet build
   ```

4. Run tests to verify the setup:

   ```bash
   dotnet test
   ```

### Quick Use

Install from NuGet and register the mediator:

```bash
dotnet add package NetEvolve.Pulse
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

var services = new ServiceCollection();

services.AddPulse(config => config.AddActivityAndMetrics());
services.AddScoped<ICommandHandler<CreateOrder, OrderCreated>, CreateOrderHandler>();

public record CreateOrder(string Sku) : ICommand<OrderCreated>;
public record OrderCreated(Guid OrderId);

public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, OrderCreated>
{
    public Task<OrderCreated> HandleAsync(CreateOrder command, CancellationToken cancellationToken) =>
        Task.FromResult(new OrderCreated(Guid.NewGuid()));
}
```

### Configuration

- Configure environment variables and connection details as required by your host application when integrating Pulse.
- Align logging and tracing setup with your OpenTelemetry configuration if using `AddActivityAndMetrics()`.
- Add custom configurators (validation, caching, retries) through `IMediatorConfigurator` extension methods.

## Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run a specific test project
dotnet test tests/NetEvolve.Pulse.Tests.Unit
```

### Code Formatting

- Use the repository analyzers and formatters configured in the solution. Run `dotnet format` if enabled in your environment.
- Address diagnostics reported in `diagnostics-*.sarif` files generated by the solution analyzers.

### Project Structure

```
src/                 # Production libraries
├── NetEvolve.Pulse
├── NetEvolve.Pulse.Extensibility
├── NetEvolve.Pulse.EntityFramework
├── NetEvolve.Pulse.SqlServer
└── NetEvolve.Pulse.Polly

tests/               # Test projects
├── NetEvolve.Pulse.Tests.Unit
├── NetEvolve.Pulse.Tests.Integration
├── NetEvolve.Pulse.EntityFramework.Tests.Integration
├── NetEvolve.Pulse.SqlServer.Tests.Integration
├── NetEvolve.Pulse.Polly.Tests.Unit
└── NetEvolve.Pulse.Polly.Tests.Integration

templates/           # Documentation templates
```

## Architecture

Pulse centers on a mediator that routes commands, queries, and events through an interceptor pipeline. Handlers run with scoped lifetimes to ensure safe resolution of dependencies per request. Interceptors can enrich context, add validation, or emit telemetry before and after handler execution. Parallel event dispatch keeps fan-out responsive while honoring cancellation tokens.

## Contributing

Contributions are welcome. Review the [Contributing Guidelines](CONTRIBUTING.md) for workflows, coding standards, and pull request expectations. Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).

## Code of Conduct

This project adheres to the [Code of Conduct](CODE_OF_CONDUCT.md). Please report unacceptable behavior through the channels defined there.

## Documentation

- [NetEvolve.Pulse project docs](src/NetEvolve.Pulse/README.md) for mediator and outbox usage
- [NetEvolve.Pulse.Extensibility docs](src/NetEvolve.Pulse.Extensibility/README.md) for contract details
- [NetEvolve.Pulse.EntityFramework docs](src/NetEvolve.Pulse.EntityFramework/README.md) for Entity Framework outbox persistence
- [NetEvolve.Pulse.SqlServer docs](src/NetEvolve.Pulse.SqlServer/README.md) for SQL Server ADO.NET outbox persistence
- [NetEvolve.Pulse.Polly docs](src/NetEvolve.Pulse.Polly/README.md) for Polly resilience policies
- [Contributing Guidelines](CONTRIBUTING.md) and [Code of Conduct](CODE_OF_CONDUCT.md)

## Versioning

This solution uses [GitVersion](https://gitversion.net/) for automated semantic versioning informed by [Conventional Commits](https://www.conventionalcommits.org/). Version numbers are derived from Git history during CI builds.

## Support

- File bugs or request features via [GitHub Issues](https://github.com/dailydevops/pulse/issues)
- Review existing documentation in this repository before opening new issues
- For security concerns, use private disclosure channels as described in the issue templates (if available)

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
