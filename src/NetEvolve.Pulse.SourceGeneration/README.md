# NetEvolve.Pulse.SourceGeneration

Roslyn source generator for the Pulse CQRS mediator library. Automatically generates DI registration code for handler classes annotated with `[PulseHandler]`, eliminating manual service registrations and catching missing or duplicate registrations at compile time.

## Installation

```bash
dotnet add package NetEvolve.Pulse.SourceGeneration
```

## Usage

Annotate your handler classes with `[PulseHandler]` and call the generated extension method:

```csharp
using NetEvolve.Pulse.SourceGeneration;
using NetEvolve.Pulse.SourceGeneration.Generated;

// In your startup code
services.AddGeneratedPulseHandlers();
```

## Diagnostics

| Id | Severity | Description |
|---|---|---|
| PULSE001 | Error | Type is annotated with `[PulseHandler]` but does not implement any known Pulse handler interface. |
| PULSE002 | Warning | Multiple `[PulseHandler]` types implement the same command or query handler contract. |
