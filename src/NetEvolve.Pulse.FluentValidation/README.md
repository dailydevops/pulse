# NetEvolve.Pulse.FluentValidation

FluentValidation interceptor for the [Pulse](https://github.com/dailydevops/pulse) CQRS mediator library.

## Overview

This package provides a `FluentValidationRequestInterceptor` that automatically validates all commands and queries before they reach their handler. It resolves all `IValidator<TRequest>` instances from the DI container, executes them, aggregates any failures, and throws a `ValidationException` if any failures exist. Requests with no registered validators pass through unchanged.

## Installation

```bash
dotnet add package NetEvolve.Pulse.FluentValidation
```

## Usage

Register the interceptor when configuring the Pulse mediator:

```csharp
services.AddPulse(c => c.AddFluentValidation());
```

Register your FluentValidation validators:

```csharp
services.AddScoped<IValidator<MyCommand>, MyCommandValidator>();
```

Or use FluentValidation's assembly scanning:

```csharp
services.AddValidatorsFromAssembly(typeof(MyCommand).Assembly);
```

## Behavior

- **No validators registered**: The request passes through to the handler unchanged.
- **Validators registered, valid input**: The request passes through to the handler.
- **Validators registered, invalid input**: A `ValidationException` is thrown before the handler executes.
- **Multiple validators**: All validators are executed and failures are aggregated into a single `ValidationException`.

## Idempotency

Calling `AddFluentValidation()` multiple times is safe — the interceptor is registered via `TryAddEnumerable` and will not be duplicated.
