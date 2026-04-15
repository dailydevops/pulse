# NetEvolve Pulse

[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](LICENSE)
[![Build Status](https://img.shields.io/github/actions/workflow/status/dailydevops/pulse/cicd.yml?branch=main)](https://github.com/dailydevops/pulse/actions)
[![Contributors](https://img.shields.io/github/contributors/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/graphs/contributors)
[![Mutation testing badge](https://img.shields.io/endpoint?style=flat&url=https%3A%2F%2Fbadge-api.stryker-mutator.io%2Fgithub.com%2Fdailydevops%2Fpulse%2Fmain)](https://dashboard.stryker-mutator.io/reports/github.com/dailydevops/pulse/main)

## Overview

NetEvolve Pulse delivers a high-performance CQRS mediator with an interceptor-enabled pipeline for commands, queries, and events. It targets .NET services that need predictable dispatching, strong typing, and first-class observability. The solution is designed for APIs, background workers, and modular libraries that require clean message handling with minimal ceremony.

## Projects

### Core Libraries

- **NetEvolve.Pulse** — Mediator implementation with interceptor pipeline, outbox pattern support, and DI integration ([src/NetEvolve.Pulse/README.md](src/NetEvolve.Pulse/README.md))
- **NetEvolve.Pulse.Extensibility** — Contracts and abstractions for commands, queries, events, handlers, and configurators ([src/NetEvolve.Pulse.Extensibility/README.md](src/NetEvolve.Pulse.Extensibility/README.md))

### Tooling

- **NetEvolve.Pulse.SourceGeneration** — Roslyn incremental source generator that automatically emits DI registration code for handler classes annotated with `[PulseHandler]`; detects missing or duplicate registrations at compile time via PULSE001 and PULSE002 diagnostics ([src/NetEvolve.Pulse.SourceGeneration/README.md](src/NetEvolve.Pulse.SourceGeneration/README.md))

### Integration Libraries

- **NetEvolve.Pulse.AspNetCore** — ASP.NET Core Minimal API integration: `IEndpointRouteBuilder` extension methods that map mediator commands and queries directly to HTTP endpoints using the `CommandHttpMethod` enum (`Post`, `Put`, `Patch`, `Delete`) ([src/NetEvolve.Pulse.AspNetCore/README.md](src/NetEvolve.Pulse.AspNetCore/README.md))
- **NetEvolve.Pulse.FluentValidation** — Automatic pre-handler validation via FluentValidation: resolves all `IValidator<TRequest>` instances and throws `ValidationException` on failure, with no impact when no validators are registered ([src/NetEvolve.Pulse.FluentValidation/README.md](src/NetEvolve.Pulse.FluentValidation/README.md))
- **NetEvolve.Pulse.HttpCorrelation** — Automatically propagates the HTTP correlation ID from `IHttpCorrelationAccessor` into every `IRequest<TResponse>` and `IEvent` dispatched through the mediator, without overwriting a caller-set value ([src/NetEvolve.Pulse.HttpCorrelation/README.md](src/NetEvolve.Pulse.HttpCorrelation/README.md))

### Provider Libraries

#### Persistence

- **NetEvolve.Pulse.EntityFramework** — Entity Framework Core persistence for the outbox pattern ([src/NetEvolve.Pulse.EntityFramework/README.md](src/NetEvolve.Pulse.EntityFramework/README.md))
- **NetEvolve.Pulse.SqlServer** — SQL Server ADO.NET persistence for the outbox pattern ([src/NetEvolve.Pulse.SqlServer/README.md](src/NetEvolve.Pulse.SqlServer/README.md))
- **NetEvolve.Pulse.PostgreSql** — PostgreSQL ADO.NET persistence for the outbox pattern using `Npgsql` with `FOR UPDATE SKIP LOCKED` for concurrent access ([src/NetEvolve.Pulse.PostgreSql/README.md](src/NetEvolve.Pulse.PostgreSql/README.md))
- **NetEvolve.Pulse.SQLite** — SQLite embedded persistence for the outbox pattern, designed for edge and CLI scenarios ([src/NetEvolve.Pulse.SQLite/README.md](src/NetEvolve.Pulse.SQLite/README.md))

#### Transport

- **NetEvolve.Pulse.AzureServiceBus** — Azure Service Bus transport for the outbox pattern with dynamic topic routing and managed identity support ([src/NetEvolve.Pulse.AzureServiceBus/README.md](src/NetEvolve.Pulse.AzureServiceBus/README.md))
- **NetEvolve.Pulse.Dapr** — Dapr pub/sub transport for the outbox pattern, publishing messages to any Dapr-supported broker via `DaprClient` ([src/NetEvolve.Pulse.Dapr/README.md](src/NetEvolve.Pulse.Dapr/README.md))
- **NetEvolve.Pulse.Kafka** — Apache Kafka transport for the outbox pattern via the Confluent.Kafka producer ([src/NetEvolve.Pulse.Kafka/README.md](src/NetEvolve.Pulse.Kafka/README.md))
- **NetEvolve.Pulse.RabbitMQ** — RabbitMQ transport for the outbox pattern via the official .NET RabbitMQ client ([src/NetEvolve.Pulse.RabbitMQ/README.md](src/NetEvolve.Pulse.RabbitMQ/README.md))

#### Resilience

- **NetEvolve.Pulse.Polly** — Polly v8 resilience policies integration for retry, circuit breaker, and timeout strategies ([src/NetEvolve.Pulse.Polly/README.md](src/NetEvolve.Pulse.Polly/README.md))

### Tests

- **NetEvolve.Pulse.Tests.Unit** — Unit coverage for all mediator behaviors, interceptors, transports, and providers ([tests/NetEvolve.Pulse.Tests.Unit](tests/NetEvolve.Pulse.Tests.Unit))
- **NetEvolve.Pulse.Tests.Integration** — Integration scenarios, pipeline validation, and database-backed outbox tests ([tests/NetEvolve.Pulse.Tests.Integration](tests/NetEvolve.Pulse.Tests.Integration))
- **NetEvolve.Pulse.SourceGeneration.Tests.Unit** — Unit tests for the Roslyn source generator, verified with snapshot testing ([tests/NetEvolve.Pulse.SourceGeneration.Tests.Unit](tests/NetEvolve.Pulse.SourceGeneration.Tests.Unit))

## Features

- Typed CQRS mediator with single-handler enforcement for commands and queries, plus fan-out event dispatch
- Interceptor pipeline for logging, metrics, tracing, validation, retries, and other cross-cutting concerns via `IMediatorConfigurator`
- **Distributed query caching** via `ICacheableQuery<TResponse>` and `AddQueryCaching()` — transparent `IDistributedCache` integration with configurable `JsonSerializerOptions` and absolute/sliding expiration via `QueryCachingOptions`
- **Outbox pattern** with background processor for reliable event delivery via `AddOutbox()`
- OpenTelemetry-friendly hooks through `AddActivityAndMetrics()` and TimeProvider-aware flows for deterministic testing and scheduling
- Minimal DI setup with `services.AddPulse(...)`, scoped lifetimes, and opt-in configurators per application
- Contracts in `NetEvolve.Pulse.Extensibility` for framework-agnostic use or deep integration with ASP.NET Core
- **ASP.NET Core Minimal API integration** via `NetEvolve.Pulse.AspNetCore`: map commands and queries directly to HTTP endpoints with `MapCommand<TCommand, TResponse>()`, `MapCommand<TCommand>()`, and `MapQuery<TQuery, TResponse>()` — no boilerplate lambdas needed
- **FluentValidation integration** via `AddFluentValidation()` — automatic pre-handler validation with failure aggregation and pass-through when no validators are registered
- **HTTP correlation propagation** via `AddHttpCorrelationEnrichment()` — zero-effort correlation ID forwarding from HTTP headers into every mediator request and event
- **Source generation** via `NetEvolve.Pulse.SourceGeneration` — `[PulseHandler]` attribute generates compile-time DI registrations with configurable lifetimes and PULSE001/PULSE002 diagnostics
- Parallel event dispatch with cancellation support to keep handlers responsive under load
- Built-in primitives like `Void` to simplify command semantics without return values
- **Multiple persistence providers**: Entity Framework Core, SQL Server ADO.NET, PostgreSQL ADO.NET, and SQLite embedded
- **Multiple transport providers**: Azure Service Bus, Dapr pub/sub, RabbitMQ, and Apache Kafka
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

```text
src/                 # Production libraries
├── NetEvolve.Pulse
├── NetEvolve.Pulse.AspNetCore
├── NetEvolve.Pulse.AzureServiceBus
├── NetEvolve.Pulse.Dapr
├── NetEvolve.Pulse.EntityFramework
├── NetEvolve.Pulse.Extensibility
├── NetEvolve.Pulse.FluentValidation
├── NetEvolve.Pulse.HttpCorrelation
├── NetEvolve.Pulse.Kafka
├── NetEvolve.Pulse.Polly
├── NetEvolve.Pulse.PostgreSql
├── NetEvolve.Pulse.RabbitMQ
├── NetEvolve.Pulse.SourceGeneration
├── NetEvolve.Pulse.SQLite
└── NetEvolve.Pulse.SqlServer

tests/               # Test projects
├── NetEvolve.Pulse.Tests.Unit
├── NetEvolve.Pulse.Tests.Integration
└── NetEvolve.Pulse.SourceGeneration.Tests.Unit

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
- [NetEvolve.Pulse.AspNetCore docs](src/NetEvolve.Pulse.AspNetCore/README.md) for Minimal API endpoint mapping
- [NetEvolve.Pulse.AzureServiceBus docs](src/NetEvolve.Pulse.AzureServiceBus/README.md) for Azure Service Bus outbox transport
- [NetEvolve.Pulse.Dapr docs](src/NetEvolve.Pulse.Dapr/README.md) for Dapr pub/sub transport
- [NetEvolve.Pulse.EntityFramework docs](src/NetEvolve.Pulse.EntityFramework/README.md) for Entity Framework outbox persistence
- [NetEvolve.Pulse.Extensibility docs](src/NetEvolve.Pulse.Extensibility/README.md) for contract details
- [NetEvolve.Pulse.FluentValidation docs](src/NetEvolve.Pulse.FluentValidation/README.md) for FluentValidation pipeline integration
- [NetEvolve.Pulse.HttpCorrelation docs](src/NetEvolve.Pulse.HttpCorrelation/README.md) for HTTP correlation ID propagation
- [NetEvolve.Pulse.Kafka docs](src/NetEvolve.Pulse.Kafka/README.md) for Apache Kafka outbox transport
- [NetEvolve.Pulse.Polly docs](src/NetEvolve.Pulse.Polly/README.md) for Polly resilience policies
- [NetEvolve.Pulse.PostgreSql docs](src/NetEvolve.Pulse.PostgreSql/README.md) for PostgreSQL ADO.NET outbox persistence
- [NetEvolve.Pulse.RabbitMQ docs](src/NetEvolve.Pulse.RabbitMQ/README.md) for RabbitMQ outbox transport
- [NetEvolve.Pulse.SourceGeneration docs](src/NetEvolve.Pulse.SourceGeneration/README.md) for source generation and `[PulseHandler]` attribute
- [NetEvolve.Pulse.SQLite docs](src/NetEvolve.Pulse.SQLite/README.md) for SQLite embedded outbox persistence
- [NetEvolve.Pulse.SqlServer docs](src/NetEvolve.Pulse.SqlServer/README.md) for SQL Server ADO.NET outbox persistence
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
