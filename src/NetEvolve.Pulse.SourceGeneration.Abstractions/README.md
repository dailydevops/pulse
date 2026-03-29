# NetEvolve.Pulse.SourceGeneration.Abstractions

Abstractions for the Pulse CQRS source generator. Contains the `PulseHandlerAttribute` used to annotate handler classes for automatic DI registration code generation at compile time.

## Installation

```bash
dotnet add package NetEvolve.Pulse.SourceGeneration.Abstractions
```

## Usage

```csharp
using NetEvolve.Pulse.SourceGeneration;
using NetEvolve.Pulse.Extensibility;

[PulseHandler]
public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, OrderResult>
{
    public Task<OrderResult> HandleAsync(
        CreateOrderCommand command, CancellationToken cancellationToken) => ...;
}
```
