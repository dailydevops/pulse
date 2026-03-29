# NetEvolve.Pulse.Testing

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.Testing.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Testing/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.Testing.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Testing/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.Testing provides a lightweight `FakeMediator` implementation for unit testing scenarios. It enables verification of mediator interactions without requiring a DI container, with support for command and query setup using canned responses or exceptions, event capture, and invocation count verification.

## Features

- **No DI Required**: Instantiate with `new FakeMediator()` — no service provider needed
- **Fluent Setup API**: Configure commands and queries with `.Returns()` or `.Throws()` chains
- **Event Capture**: Record published events and inspect them in assertion order
- **Invocation Verification**: Assert exact call counts for commands, queries, and events
- **Thread-Safe**: All internal state is managed with concurrent collections

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.Testing
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.Testing
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.Testing" Version="x.x.x" />
```

## Quick Start

```csharp
using NetEvolve.Pulse.Testing;

var mediator = new FakeMediator();

// Setup a command to return a canned response
mediator.SetupCommand<CreateOrderCommand, OrderResult>()
    .Returns(new OrderResult { Id = "123" });

// Use in your system under test
var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(
    new CreateOrderCommand("SKU-001"));

// Verify the command was called exactly once
mediator.Verify<CreateOrderCommand>(times: 1);
```

## Usage

### Command Setup

```csharp
// Return a canned response
mediator.SetupCommand<CreateOrderCommand, OrderResult>()
    .Returns(new OrderResult { Id = "123" });

// Throw a specific exception
mediator.SetupCommand<CreateOrderCommand, OrderResult>()
    .Throws(new InvalidOperationException("Duplicate order"));

// Throw by exception type
mediator.SetupCommand<CreateOrderCommand, OrderResult>()
    .Throws<TimeoutException>();
```

### Query Setup

```csharp
mediator.SetupQuery<GetOrderQuery, OrderDto>()
    .Returns(new OrderDto { Id = "123", Status = "Completed" });
```

### Void Commands

```csharp
mediator.SetupCommand<DeleteOrderCommand, Void>()
    .Returns(Void.Completed);

await mediator.SendAsync(new DeleteOrderCommand("123"));
```

### Event Capture

```csharp
mediator.SetupEvent<OrderCreatedEvent>();

await mediator.PublishAsync(new OrderCreatedEvent { OrderId = "123" });

var events = mediator.GetPublishedEvents<OrderCreatedEvent>();
Assert.Single(events);
Assert.Equal("123", events[0].OrderId);
```

### Invocation Verification

```csharp
mediator.Verify<CreateOrderCommand>(times: 1);  // Passes
mediator.Verify<CreateOrderCommand>(times: 2);  // Throws InvalidOperationException
```

### Fluent Chaining

```csharp
var mediator = new FakeMediator();

mediator
    .SetupCommand<CreateOrderCommand, OrderResult>()
        .Returns(new OrderResult { Id = "123" })
    .SetupQuery<GetOrderQuery, OrderDto>()
        .Returns(new OrderDto { Id = "123" })
    .SetupEvent<OrderCreatedEvent>();
```

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0

## Contributing

Contributions are welcome! Please read the [Contributing Guidelines](https://github.com/dailydevops/pulse/blob/main/CONTRIBUTING.md) before submitting a pull request.

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core CQRS mediator
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Extensibility contracts
- [**NetEvolve.Pulse.Polly**](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/) - Polly resilience integration

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
