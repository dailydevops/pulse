# NetEvolve.Pulse.Polly

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.Polly.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.Polly.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse.Polly provides Polly v8 resilience policies for the Pulse CQRS mediator through interceptor integration. Add retry, circuit breaker, timeout, bulkhead, and fallback strategies to command handlers, query handlers, and event handlers with fluent API configuration.

## Features

* **Polly v8 Integration**: Seamless integration with Polly's modern resilience pipeline API
* **Per-Handler Policies**: Fine-grained control over resilience strategies for specific handlers
* **Global Policies**: Apply policies across all requests or events
* **Multiple Policy Types**: Retry, circuit breaker, timeout, bulkhead, and fallback strategies
* **Fluent API**: Type-safe configuration through extension methods on `IMediatorConfigurator`
* **LIFO-Aware**: Works with Pulse's interceptor execution order for predictable behavior
* **Thread-Safe**: Polly pipelines are singleton-safe and designed for concurrent use

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.Polly
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.Polly
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.Polly" Version="x.x.x" />
```

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Polly;
using Polly;

var services = new ServiceCollection();

services.AddPulse(config => config
    .AddCommandHandler<CreateOrderCommand, OrderResult, CreateOrderHandler>()
    .AddPollyRequestPolicies<CreateOrderCommand, OrderResult>(pipeline => pipeline
        .AddRetry(new RetryStrategyOptions<OrderResult>
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential
        })));

using var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

// Handler execution is protected by retry policy
var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(
    new CreateOrderCommand("SKU-123"));
```

## Usage

### Per-Handler Retry Policy

Apply retry logic to a specific command or query handler:

```csharp
services.AddPulse(config => config
    .AddCommandHandler<CreateOrderCommand, OrderResult, CreateOrderHandler>()
    .AddPollyRequestPolicies<CreateOrderCommand, OrderResult>(pipeline => pipeline
        .AddRetry(new RetryStrategyOptions<OrderResult>
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            OnRetry = args =>
            {
                Console.WriteLine($"Retry attempt {args.AttemptNumber}");
                return default;
            }
        })));
```

### Circuit Breaker for External Dependencies

Protect external service calls with a circuit breaker:

```csharp
services.AddPulse(config => config
    .AddQueryHandler<GetUserQuery, User, GetUserQueryHandler>()
    .AddPollyRequestPolicies<GetUserQuery, User>(pipeline => pipeline
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions<User>
        {
            FailureRatio = 0.5,              // Break after 50% failures
            MinimumThroughput = 10,          // Minimum 10 requests in window
            BreakDuration = TimeSpan.FromSeconds(30),
            SamplingDuration = TimeSpan.FromMinutes(1),
            OnOpened = args =>
            {
                Console.WriteLine("Circuit breaker opened!");
                return default;
            }
        })));
```

### Combined Policies (Timeout + Retry + Circuit Breaker)

Layer multiple resilience strategies:

```csharp
services.AddPulse(config => config
    .AddQueryHandler<SearchProductsQuery, ProductList, SearchProductsHandler>()
    .AddPollyRequestPolicies<SearchProductsQuery, ProductList>(pipeline => pipeline
        .AddTimeout(TimeSpan.FromSeconds(30))         // Outermost: Total timeout
        .AddRetry(new RetryStrategyOptions<ProductList>
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential
        })                                            // Middle: Retry transient failures
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions<ProductList>
        {
            FailureRatio = 0.7,
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(15)
        })));                                         // Innermost: Circuit breaker
```

### Void Commands

For commands that don't return a response:

```csharp
services.AddPulse(config => config
    .AddCommandHandler<DeleteOrderCommand, DeleteOrderHandler>()
    .AddPollyRequestPolicies<DeleteOrderCommand>(pipeline => pipeline
        .AddRetry(new RetryStrategyOptions<Void>
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromSeconds(1)
        })
        .AddTimeout(TimeSpan.FromSeconds(10))));
```

### Event Handler Policies

Apply policies to event processing:

```csharp
services.AddPulse(config => config
    .AddEventHandler<OrderCreatedEvent, SendEmailHandler>()
    .AddEventHandler<OrderCreatedEvent, UpdateInventoryHandler>()
    .AddPollyEventPolicies<OrderCreatedEvent>(pipeline => pipeline
        .AddTimeout(TimeSpan.FromSeconds(10))
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.7,
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(15)
        })));
```

**⚠️ Warning**: Event policies apply to all handlers for that event type. If the policy triggers a retry, all handlers will re-execute. Consider using `IEventOutbox` for reliable event delivery instead of aggressive retries.

### Global Policies

Apply policies to all requests or events:

```csharp
services.AddPulse(config => config
    // Global timeout for all requests
    .AddDefaultPollyRequestPolicies(pipeline => pipeline
        .AddTimeout(TimeSpan.FromMinutes(5)))
    
    // Global timeout for all events
    .AddDefaultPollyEventPolicies(pipeline => pipeline
        .AddTimeout(TimeSpan.FromSeconds(30)))
    
    // Register handlers
    .AddCommandHandler<CreateOrderCommand, OrderResult, CreateOrderHandler>()
    .AddQueryHandler<GetUserQuery, User, GetUserQueryHandler>()
    .AddEventHandler<OrderCreatedEvent, NotificationHandler>());
```

### Bulkhead Isolation

Limit concurrent executions to prevent resource exhaustion:

```csharp
services.AddPulse(config => config
    .AddCommandHandler<ImportDataCommand, ImportResult, ImportDataHandler>()
    .AddPollyRequestPolicies<ImportDataCommand, ImportResult>(pipeline => pipeline
        .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 5,                          // Max 5 concurrent executions
            QueueLimit = 10                           // Queue up to 10 waiting requests
        })));
```

### Fallback Strategy

Provide alternative responses on failure:

```csharp
services.AddPulse(config => config
    .AddQueryHandler<GetCachedDataQuery, DataResult, GetCachedDataHandler>()
    .AddPollyRequestPolicies<GetCachedDataQuery, DataResult>(pipeline => pipeline
        .AddFallback(new FallbackStrategyOptions<DataResult>
        {
            FallbackAction = args => Outcome.FromResultAsValueTask(
                new DataResult { IsFromCache = true, Data = "Default" })
        })));
```

## Policy Execution Order

Pulse interceptors execute in **LIFO (Last-In, First-Out)** order. The last registered interceptor runs first. Plan your policy chain accordingly:

```csharp
config
    .AddCommandHandler<CreateOrder, Result, CreateOrderHandler>()
    .AddValidationInterceptor<CreateOrder, Result>()   // Executes third (innermost)
    .AddPollyRequestPolicies<CreateOrder, Result>(...)        // Executes second
    .AddActivityAndMetrics();                          // Executes first (outermost)
```

Within a single Polly pipeline, strategies execute in the order they are added:

```csharp
pipeline
    .AddTimeout(...)         // Outermost strategy
    .AddRetry(...)          // Middle strategy
    .AddCircuitBreaker(...) // Innermost strategy
```

## Best Practices

### Retry Policies

* Use **exponential backoff** for transient failures (network, database connections)
* Keep `MaxRetryAttempts` conservative (2-3 for most scenarios)
* Add jitter to prevent thundering herd: `UseJitter = true`
* Log retry attempts for observability

### Circuit Breakers

* Apply to **external dependencies** (APIs, databases, message queues)
* Set realistic `FailureRatio` (0.5-0.7) and `MinimumThroughput` values
* Monitor circuit breaker state transitions for alerts
* Use separate circuit breakers per dependency

### Timeouts

* Set based on **P99 latency + retry overhead**
* Use shorter timeouts for events than requests
* Consider async operations - timeout should exceed sum of all downstream calls
* Combine with cancellation tokens for proper cleanup

### Bulkhead

* Use for **resource-intensive operations** (file processing, heavy computations)
* Set `PermitLimit` based on available resources (CPU cores, memory)
* Monitor queue saturation for capacity planning

### Performance

* Register pipelines with **Singleton lifetime** (default) for optimal performance
* Polly pipelines are thread-safe and stateless (except circuit breaker state)
* Reuse pipelines across requests - avoid creating per-request instances
* Profile policy overhead in production scenarios

### Events

* Be **conservative with retry policies** on events (multiple handlers amplify effects)
* Use shorter timeouts than requests to keep event processing responsive
* Consider `IEventOutbox` pattern for guaranteed delivery vs. aggressive retries
* Monitor event handler failures separately from request failures

## Advanced Scenarios

### Per-Handler Policy Configuration with Keyed Services

For different policies per handler type, use keyed services:

```csharp
services.AddKeyedSingleton("critical", sp =>
{
    var builder = new ResiliencePipelineBuilder<OrderResult>();
    builder.AddRetry(new RetryStrategyOptions<OrderResult> { MaxRetryAttempts = 5 });
    return builder.Build();
});

services.AddKeyedSingleton("standard", sp =>
{
    var builder = new ResiliencePipelineBuilder<OrderResult>();
    builder.AddRetry(new RetryStrategyOptions<OrderResult> { MaxRetryAttempts = 2 });
    return builder.Build();
});
```

### Combining Global and Per-Handler Policies

Global policies wrap per-handler policies:

```csharp
services.AddPulse(config => config
    // Global timeout applies to all
    .AddDefaultPollyRequestPolicies(pipeline => pipeline
        .AddTimeout(TimeSpan.FromMinutes(5)))
    
    // Per-handler retry within global timeout
    .AddCommandHandler<CreateOrder, Result, CreateOrderHandler>()
    .AddPollyRequestPolicies<CreateOrder, Result>(pipeline => pipeline
        .AddRetry(new RetryStrategyOptions<Result> { MaxRetryAttempts = 3 })));
```

## Telemetry and Monitoring

Polly v8 provides built-in telemetry through `System.Diagnostics`:

```csharp
// Polly emits metrics to these meter names:
// - Polly.Retry
// - Polly.CircuitBreaker
// - Polly.Timeout
// - Polly.RateLimiter

// Example: Monitor circuit breaker state
var meterListener = new MeterListener();
meterListener.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == "Polly.CircuitBreaker")
    {
        listener.EnableMeasurementEvents(instrument, null);
    }
};
```

For integration with Pulse's `AddActivityAndMetrics()`, policy overhead is included in handler execution time.

## Comparison with Other Approaches

| Approach | Pros | Cons |
|----------|------|------|
| **Polly Interceptors** | Declarative, reusable, testable, composable with other interceptors | LIFO ordering requires planning |
| **Manual Polly in Handlers** | Fine-grained control, explicit | Repetitive code, hard to test, scattered logic |
| **Middleware/Filters** | Request-level scope | Not handler-specific, can't differentiate commands/queries |

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](../../CONTRIBUTING.md) for details.

## License

This project is licensed under the MIT License - see the [LICENSE](../../LICENSE) file for details.

## Related Packages

* [NetEvolve.Pulse](../NetEvolve.Pulse/README.md) - Core CQRS mediator
* [NetEvolve.Pulse.Extensibility](../NetEvolve.Pulse.Extensibility/README.md) - Extensibility contracts
* [Polly](https://github.com/App-vNext/Polly) - Resilience and transient-fault-handling library
