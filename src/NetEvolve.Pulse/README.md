# NetEvolve.Pulse

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.svg)](https://www.nuget.org/packages/NetEvolve.Pulse/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.svg)](https://www.nuget.org/packages/NetEvolve.Pulse/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

NetEvolve.Pulse is a high-performance CQRS mediator for ASP.NET Core that wires commands, queries, and events through a scoped, interceptor-enabled pipeline.

## Features

- Typed CQRS mediator with single-handler enforcement for commands and queries plus fan-out events
- Minimal DI integration via `services.AddPulse(...)` with scoped lifetimes for handlers and interceptors
- Configurable interceptor pipeline (logging, metrics, tracing, validation) via `IMediatorBuilder`
- **Distributed query caching** — register `ICacheableQuery<TResponse>` per query and enable transparent `IDistributedCache` caching with `AddQueryCaching()`
- **Outbox pattern** with background processor for reliable event delivery via `AddOutbox()`
- Parallel event dispatch for efficient domain event broadcasting
- TimeProvider-aware for deterministic testing and scheduling scenarios
- OpenTelemetry-friendly metrics and tracing through `AddActivityAndMetrics()`

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse" Version="x.x.x" />
```

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

var services = new ServiceCollection();

// Register Pulse and handlers
services.AddPulse();
services.AddScoped<ICommandHandler<CreateOrderCommand, OrderCreated>, CreateOrderHandler>();

using var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

var result = await mediator.SendAsync<CreateOrderCommand, OrderCreated>(
    new CreateOrderCommand("SKU-123"));

Console.WriteLine($"Created order {result.OrderId}");

public record CreateOrderCommand(string Sku) : ICommand<OrderCreated>;
public record OrderCreated(Guid OrderId);

public sealed class CreateOrderHandler
    : ICommandHandler<CreateOrderCommand, OrderCreated>
{
    public Task<OrderCreated> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(new OrderCreated(Guid.NewGuid()));
}
```

## Usage

### Basic Example

```csharp
services.AddPulse();
services.AddScoped<IQueryHandler<GetOrderQuery, Order>, GetOrderHandler>();
services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();

var order = await mediator.QueryAsync<GetOrderQuery, Order>(new GetOrderQuery(orderId));
await mediator.PublishAsync(new OrderCreatedEvent(order.Id));

public record GetOrderQuery(Guid Id) : IQuery<Order>;
public record Order(Guid Id, string Sku);
public record OrderCreatedEvent(Guid Id) : IEvent;

public sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, Order>
{
    public Task<Order> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new Order(query.Id, "SKU-123"));
}

public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // React to the event (logging, projections, etc.)
        return Task.CompletedTask;
    }
}
```

### Advanced Example

```csharp
// Enable tracing and metrics and add custom interceptors
services.AddPulse(config =>
{
    config.AddActivityAndMetrics();
});

services.AddScoped<ICommandHandler<ShipOrderCommand, Void>, ShipOrderHandler>();

public record ShipOrderCommand(Guid Id) : ICommand;

public sealed class ShipOrderHandler : ICommandHandler<ShipOrderCommand, Void>
{
    public Task<Void> HandleAsync(ShipOrderCommand command, CancellationToken cancellationToken)
    {
        // Shipping workflow here
        return Task.FromResult(Void.Completed);
    }
}
```

## Configuration

```csharp
// Configure Pulse during startup
services.AddPulse(config =>
{
    // Built-in observability
    config.AddActivityAndMetrics();

    // Add your own configurator extensions for validation, caching, etc.
    // config.AddCustomValidation();
});
```

### Distributed Query Caching

Enable transparent `IDistributedCache` caching for queries. Any query that implements `ICacheableQuery<TResponse>` (from `NetEvolve.Pulse.Extensibility`) is served from the cache on subsequent invocations; all other queries pass through unchanged.

```csharp
// 1. Register an IDistributedCache implementation
services.AddDistributedMemoryCache(); // or Redis, SQL Server, etc.

// 2. Enable the caching interceptor (with optional options)
services.AddPulse(config => config.AddQueryCaching(options =>
{
    // Choose between absolute (default) and sliding expiration
    options.ExpirationMode = CacheExpirationMode.Sliding;

    // Supply custom JsonSerializerOptions for cache serialization
    options.JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}));

// 3. Implement ICacheableQuery<TResponse> on the queries you want cached
public record GetProductQuery(Guid Id) : ICacheableQuery<ProductDto>
{
    public string? CorrelationId { get; set; }

    // Unique per query result — include all discriminating parameters
    public string CacheKey => $"product:{Id}";

    // null = no explicit expiry (relies on cache defaults); or provide a TimeSpan
    public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
}
```

Behavior summary:

| Scenario | Result |
| --- | --- |
| Query implements `ICacheableQuery<TResponse>` and cache entry exists | Cached response returned; handler skipped |
| Query implements `ICacheableQuery<TResponse>` and no cache entry | Handler invoked; response stored in cache |
| Query does **not** implement `ICacheableQuery<TResponse>` | Handler always invoked; cache never consulted |
| `IDistributedCache` not registered in DI | Interceptor falls through; handler invoked without error |
| `Expiry = null` and `DefaultExpiry = null` | Entry stored without explicit expiry; cache default eviction policy applies |
| `Expiry = null` and `DefaultExpiry` is set | `DefaultExpiry` value is applied using the configured `ExpirationMode` |
| `ExpirationMode = Absolute` (default) | `Expiry` (or `DefaultExpiry`) is applied as absolute expiry relative to now |
| `ExpirationMode = Sliding` | `Expiry` (or `DefaultExpiry`) window resets on each cache access |

### Request Timeouts

Enforce a per-request deadline for commands and queries that implement `ITimeoutRequest` (from `NetEvolve.Pulse.Extensibility`). All other requests pass through unchanged.

```csharp
// Optional global fallback for ITimeoutRequest implementations that return a null Timeout
services.AddPulse(config => config.AddRequestTimeout(TimeSpan.FromSeconds(30)));

public record ProcessOrderCommand(string OrderId) : ICommand<OrderResult>, ITimeoutRequest
{
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }

    public TimeSpan? Timeout => TimeSpan.FromSeconds(10);
}
```

Behavior summary:

| Scenario | Result |
| --- | --- |
| Handler completes within the deadline | Result returned |
| Handler observes the cancelled token after the deadline | `TimeoutException` |
| Handler ignores the token and returns after the deadline | `TimeoutException`; the late result is discarded |
| Caller cancels the `CancellationToken` | `OperationCanceledException` (never `TimeoutException`) |
| `Timeout` and `GlobalTimeout` are `null`, or `Timeout` is `Timeout.InfiniteTimeSpan` | Pass-through without deadline |
| Request does **not** implement `ITimeoutRequest` | Pass-through without deadline |

The deadline is scheduled and measured with the registered `TimeProvider`, so it can be controlled in tests.

> **Side effects:** the interceptor cannot undo work. A command handler that finished after the deadline may already have written data, published events or called external systems before the `TimeoutException` is thrown. Any retry policy that reacts to a `TimeoutException` must therefore be idempotent (for example by combining it with `IIdempotentCommand<TResponse>` or natural idempotency keys).

### Audit Trail

`AddAudit()` records every command, and optionally every query, to the registered `IAuditStore` after the handler has run. Register a store with one of the provider-specific `Add*AuditStore()` extensions; without a store the interceptor does nothing.

```csharp
services.AddPulse(config => config.AddAudit(options => options.CapturePayload = true));
```

Audit writes are best effort (fail open):

| Scenario | Result |
| --- | --- |
| Handler completes | `AuditResult.Success` recorded, result returned |
| Handler throws | `AuditResult.Failure` recorded with the handler's exception message, original exception rethrown |
| Store, serializer or user accessor throws after a successful handler | Error logged, result returned, no `Failure` record |
| Store, serializer or user accessor throws after a failing handler | Error logged, original handler exception rethrown |
| Caller cancels the `CancellationToken` after the handler finished | Record still written (`CancellationToken.None`), no `OperationCanceledException` |

A missing audit record therefore shows up as an `Error` log entry, never as a failed request.

### Outbox Pattern Configuration

The outbox pattern ensures reliable event delivery by persisting events before dispatching:

```csharp
services.AddPulse(config => config
    .AddOutbox(
        options => options.Schema = "pulse",
        processorOptions =>
        {
            processorOptions.BatchSize = 100;              // Messages per batch (default: 100)
            processorOptions.PollingInterval = TimeSpan.FromSeconds(5);  // Poll delay and, without exponential backoff, retry delay (default: 5s)
            processorOptions.MaxRetryCount = 3;            // Max delivery attempts incl. the first before dead letter (default: 3, min: 1)
            processorOptions.ProcessingTimeout = TimeSpan.FromSeconds(30); // Per-message timeout (default: 30s)
            processorOptions.EnableBatchSending = false;   // Use batch transport (default: false)
        })
    // Choose a persistence provider:
    // .AddEntityFrameworkOutbox<MyDbContext>()
    // .AddSqlServerOutbox(connectionString)
);
```

#### Per-Event-Type Overrides

You can tune processing behaviour for individual event types using `EventTypeOverrides`. The dictionary key is the event `Type` and matches the `EventType` of stored outbox messages. Any `null` property falls back to the global default:

```csharp
processorOptions.EventTypeOverrides[typeof(CriticalEvent)] = new OutboxEventTypeOptions
{
    MaxRetryCount = 10,                         // More delivery attempts for critical events
    ProcessingTimeout = TimeSpan.FromSeconds(10), // Tighter timeout
};

processorOptions.EventTypeOverrides[typeof(BulkEvent)] = new OutboxEventTypeOptions
{
    MaxRetryCount = 1,                          // Single attempt, no retries for low-priority bulk events
    ProcessingTimeout = TimeSpan.FromMinutes(2), // Longer timeout for large payloads
};
```

See [NetEvolve.Pulse.EntityFramework](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) or [NetEvolve.Pulse.SqlServer](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) for persistence provider setup.

### Payload Serialization

Pulse uses `IPayloadSerializer` (from `NetEvolve.Pulse.Extensibility`) for all internal serialization needs, including outbox message payloads, distributed cache entries, and audit trail data. A default implementation based on System.Text.Json is registered automatically when you call `AddPulse()`.

#### Default Behavior

No configuration is required — the built-in `SystemTextJsonPayloadSerializer` uses default `JsonSerializerOptions`:

```csharp
services.AddPulse();
// SystemTextJsonPayloadSerializer is automatically registered
```

#### Configure JSON Serialization Options

Use the standard .NET options pattern to customize JSON serialization settings:

```csharp
services.Configure<JsonSerializerOptions>(options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.WriteIndented = false;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
services.AddPulse();
```

These options will be used for all payload serialization throughout Pulse, including:
- Outbox message payloads
- Distributed cache query results
- Any other internal serialization needs

#### Custom Serializer Implementation

Replace the default serializer with your own implementation by registering it before calling `AddPulse()`:

```csharp
using NetEvolve.Pulse.Extensibility;

// Register custom serializer (e.g., using Newtonsoft.Json)
services.AddSingleton<IPayloadSerializer, NewtonsoftJsonPayloadSerializer>();
services.AddPulse();

public sealed class NewtonsoftJsonPayloadSerializer : IPayloadSerializer
{
    public string Serialize<T>(T value) => 
        JsonConvert.SerializeObject(value);

    public string Serialize(object value, Type type) => 
        JsonConvert.SerializeObject(value, type, null);

    public byte[] SerializeToBytes<T>(T value) => 
        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));

    public T? Deserialize<T>(string payload) => 
        JsonConvert.DeserializeObject<T>(payload);

    public T? Deserialize<T>(byte[] payload) => 
        JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(payload));
}
```

The custom serializer will be used for all payload operations within Pulse. Ensure your implementation is thread-safe, as the same instance may be accessed concurrently from multiple pipeline stages.

## Telemetry

`AddActivityAndMetrics()` emits activities and metrics on the `NetEvolve.Pulse` activity source and meter, following the [OpenTelemetry recording errors conventions](https://opentelemetry.io/docs/specs/semconv/general/recording-errors/):

* Successful operations leave the activity status `Unset`. Failed operations set `Error` with the exception message.
* Failed operations carry `error.type` (the full exception type name) on the activity, the error counter and the duration histogram.
* A stream query records its duration once for every outcome. A stream whose consumer stops early (`break`, `Take`) carries `pulse.stream.completed=false` instead of `pulse.success`.
* A stream query whose handler honours a cancelled token, for example `HttpContext.RequestAborted` after a client disconnects, fails with `OperationCanceledException` and is recorded as a failure with `error.type=System.OperationCanceledException`. An exception from the inner enumerator's `DisposeAsync` is also recorded as a failure, unless the stream had already faulted; the earlier exception is then both thrown and recorded.

### Semantic Convention Units

Metrics keep their legacy units by default. Opt into the units of the [OpenTelemetry metrics guidelines](https://opentelemetry.io/docs/specs/semconv/general/metrics/#instrument-units) once your dashboards and alerts are migrated:

```csharp
services.AddPulse(config => config.AddActivityAndMetrics(options => options.UseSemanticConventionUnits = true));
```

The option also applies to the outbox processor metrics. Without `AddActivityAndMetrics`, configure it with `services.Configure<ActivityAndMetricsOptions>(...)`.

| Instrument | Default unit | With `UseSemanticConventionUnits` |
| --- | --- | --- |
| `pulse.requests.total` | `requests` | `{request}` |
| `pulse.events.total` | `events` | `{event}` |
| `pulse.stream_query.total` | `queries` | `{query}` |
| `pulse.request.errors`, `pulse.event.errors`, `pulse.stream_query.errors` | `errors` | `{error}` |
| `pulse.outbox.processed.total`, `pulse.outbox.failed.total`, `pulse.outbox.deadletter.total`, `pulse.outbox.pending` | `messages` | `{message}` |
| `pulse.request.duration`, `pulse.event.duration`, `pulse.stream_query.duration`, `pulse.outbox.processing.duration` | `ms` (milliseconds) | `s` (seconds, with bucket boundaries from 0.005 to 10 s) |

Migration steps:

1. Replace filters on the `Ok` span status with "status is not `Error`".
2. Enable `UseSemanticConventionUnits`. Exporters that append the unit to the metric name (for example the Prometheus exporter) then export new names such as `pulse_request_duration_seconds` instead of `pulse_request_duration_milliseconds`.
3. Update dashboards and alerts to the new names and convert duration thresholds from milliseconds to seconds.

A later `0.x` release makes the semantic convention units the default.

## NativeAOT and Trimming

All Pulse runtime packages are built with `IsAotCompatible` enabled, so the trim and NativeAOT analyzers run on every build. The core mediator pipeline (`AddPulse`, handler registration through `NetEvolve.Pulse.SourceGeneration` or the generic `Add*Handler<,>` methods, `SendAsync`, `QueryAsync`, `StreamQueryAsync` and `PublishAsync`) is trim- and NativeAOT-safe. This is verified on every pull request by publishing and running the `samples/NetEvolve.Pulse.Xample.Aot` smoke application with NativeAOT for `net8.0`, `net9.0` and `net10.0`. It covers commands, queries, stream queries and events, open-generic handlers and interceptors, value-type and `Void` requests through the built-in interceptors, the outbox event type round-trip and the payload serializer setup below.

### Value-Type Requests Under NativeAOT

The DI container cannot close open-generic services over value types under NativeAOT. For every handled command, query or stream query with a value-type request or response type, including `Void`, the method generated by `NetEvolve.Pulse.SourceGeneration` therefore calls `NativeAotInterceptorExtensions` when the application references `NetEvolve.Pulse`. Under NativeAOT, these calls register closed variants of the built-in open-generic interceptors that are registered at that time, together with closed interceptors for the same request type, and the mediator resolves them for these requests. Call the generated method after `AddPulse` and after every other interceptor registration, such as closed application interceptors or Polly policies added by a later `AddPulse` call:

```csharp
services.AddPulse(config => config.AddActivityAndMetrics().AddLogging());
services.AddScoped<IRequestInterceptor<GetStockQuery, int>, StockAuditInterceptor>();
services.AddMyProjectPulseHandlers(); // after every interceptor registration
```

* When an interceptor for such a request is registered, replaced or removed after the generated method ran, the mediator throws an `InvalidOperationException` for that request under NativeAOT instead of skipping the interceptor. Request types without any interceptor registration at that time are not affected.

* The closed registrations cover the built-in interceptors of `NetEvolve.Pulse`: activity and metrics, audit, cache invalidation, command dead letters, concurrent command guard, DataAnnotations, idempotency, logging, query caching and timeouts.
* When another open-generic interceptor is registered, for example by `AddFluentValidation`, `AddHttpCorrelationEnrichment` or the application, nothing is closed for the request type and its resolution keeps failing, instead of silently skipping that interceptor.
* Handlers registered without the source generator can call `services.AddNativeAotCommandInterceptors<TCommand, TResponse>()`, `AddNativeAotExclusiveCommandInterceptors`, `AddNativeAotQueryInterceptors` or `AddNativeAotStreamQueryInterceptors` after `AddPulse` and after every other interceptor registration.
* Outside NativeAOT, these methods register nothing, because the DI container closes open-generic services over value types itself.

### Payload Serialization Under NativeAOT

`SystemTextJsonPayloadSerializer` resolves contracts through `JsonSerializerOptions.GetTypeInfo`. Reflection-based serialization is disabled by default in trimmed and NativeAOT applications, so register a source-generated `JsonSerializerContext` for your payload types (outbox events, cached query responses, audited and dead-lettered commands):

```csharp
[JsonSerializable(typeof(OrderCreatedEvent))]
[JsonSerializable(typeof(OrderDto))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

services.Configure<JsonSerializerOptions>(options => options.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
services.AddPulse();
```

Pulse never falls back to reflection in these applications. Serializing a type that no registered context covers throws a `NotSupportedException`.

The Azure Queue Storage transport writes its envelope with the same `JsonSerializerOptions` and appends an internal source-generated context for the envelope, so no application contract is needed for it.

### APIs That Are Not Trim- or NativeAOT-Safe

The following public APIs carry `[RequiresUnreferencedCode]` (and `[RequiresDynamicCode]` where noted), so the compiler warns when a trimmed or NativeAOT application calls them:

| API | Package | Annotations | Reason |
| --- | --- | --- | --- |
| `AddHandlersFromAssembly`, `AddHandlersFromAssemblies`, `AddInterceptorsFromAssembly` and the other assembly scanning methods | `NetEvolve.Pulse` | RUC, RDC | Enumerate assembly types and close generic types at runtime. Use the source generator instead. |
| `AddDataAnnotations` | `NetEvolve.Pulse` | RUC | `Validator.TryValidateObject` reflects over the properties and attributes of the validated types. |
| `ICommandDeadLetterManagement.ReplayAsync` and `CommandDeadLetterReplayDispatcher.ReplayAsync` (all providers) | `NetEvolve.Pulse.Extensibility`, providers | RUC, RDC | Resolve the persisted command type by name and dispatch it through `MakeGenericMethod`. |
| `MapCommand`, `MapQuery`, `MapStreamQuery` | `NetEvolve.Pulse.AspNetCore` | RUC, RDC | Build request delegates with `RequestDelegateFactory` over the application's request types. |
| `MapOutboxInspector`, `MapAuditInspector`, `MapCommandDeadLetterInspector` | `NetEvolve.Pulse.AspNetCore` | RUC, RDC | Build request delegates with `RequestDelegateFactory`. Their responses use the application's `HttpJsonOptions` with the internal source-generated `PulseInspectorJsonSerializerContext` appended as fallback resolver, so they need no reflection. Enums are written as numbers unless the application registers a converter such as `JsonStringEnumConverter<TEnum>`. |

### Justified Suppressions

Pulse suppresses a trim warning only where the reflected value is used safely:

* The outbox repositories and management implementations (SQL Server, PostgreSQL, MySQL, SQLite, MongoDB, Cosmos DB and Entity Framework Core) resolve the persisted event type through `OutboxEventTypeResolver` in `NetEvolve.Pulse.Extensibility`, which calls `Type.GetType` and holds the only suppression (IL2057). The resolved type is only used for its identity. This works for every event type that is compiled into the application reading the outbox, for example because the application publishes it. The NativeAOT smoke application verifies the round-trip for an event type that is only published and never named through `typeof`. A separate outbox processor that never references an event type MUST root it, for example with `typeof(OrderCreatedEvent)` or `[DynamicDependency]`, otherwise the trimmer removes it and its messages are dead-lettered. A message whose event type cannot be resolved does not fail the fetch: the repositories move it to the dead-letter state with the error `Cannot resolve event type '<stored name>'.` and process the remaining messages ([#772](https://github.com/dailydevops/pulse/issues/772)). The management API and the outbox inspector still list such a message with its stored type name, so it can be replayed once the type is available again, or dismissed.
* The DataAnnotations interceptors (IL2026) are only registered through the annotated `AddDataAnnotations`. `NativeAotInterceptorExtensions` only closes them when that open-generic registration exists.
* `XmlDocumentationReader` in `NetEvolve.Pulse.AspNetCore` reads `Assembly.Location` (IL3000) and already returns no summary when the location is empty in single-file applications.
* `SystemTextJsonPayloadSerializer` adds the reflection resolver only while the `JsonSerializer.IsReflectionEnabledByDefault` feature switch is enabled, which is never the case in trimmed or NativeAOT applications.

### Known Limitations

* The DI container cannot close open-generic services over value types under NativeAOT, so requests with a value-type request or response type, including `Void` for commands without a result, cannot resolve open-generic interceptors directly. For handlers registered by `NetEvolve.Pulse.SourceGeneration`, the generated method registers closed variants of the built-in interceptors of `NetEvolve.Pulse` (see [Value-Type Requests Under NativeAOT](#value-type-requests-under-nativeaot)). Open-generic interceptors of other packages, such as `AddFluentValidation` or `AddHttpCorrelationEnrichment`, or of the application still fail for these requests. Use reference-type responses for such requests, or register closed interceptor implementations.
* `MapStreamQueryHub` and `PulseStreamHub<TQuery, TResponse>` build without trim or AOT warnings, but SignalR itself is not supported under NativeAOT on .NET 8 and only partially supported on .NET 9 and later. Under NativeAOT, register a source-generated `JsonSerializerContext` for `TQuery` and `TResponse` with the JSON hub protocol, for example `services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default))`, and observe the [SignalR NativeAOT restrictions](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-9.0#signalr).
* `NetEvolve.Pulse.AspNetCore.Grpc` is trim- and NativeAOT-compatible, like gRPC for ASP.NET Core itself. `MapStreamQueryGrpc` carries the `DynamicallyAccessedMembers` requirement of `MapGrpcService`.
* Provider packages inherit the NativeAOT support of their dependencies. Entity Framework Core, the MongoDB and Cosmos DB drivers, MySql.Data and the Dapr client are not fully NativeAOT-compatible.

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- ASP.NET Core environment with `Microsoft.Extensions.DependencyInjection`
- OpenTelemetry packages when using `AddActivityAndMetrics()`

## Related Packages

- [**NetEvolve.Pulse.Dapr**](https://www.nuget.org/packages/NetEvolve.Pulse.Dapr/) - Dapr pub/sub integration for event dispatch
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Core contracts and abstractions used by the mediator
- [**NetEvolve.Pulse.EntityFramework**](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework Core persistence for the outbox pattern
- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) - SQL Server ADO.NET persistence for the outbox pattern
- [**NetEvolve.Pulse.Polly**](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/) - Polly v8 resilience policies integration

## Documentation

For complete documentation, please visit the [official documentation](https://github.com/dailydevops/pulse/blob/main/README.md).

## Contributing

Contributions are welcome! Please read the [Contributing Guidelines](https://github.com/dailydevops/pulse/blob/main/CONTRIBUTING.md) before submitting a pull request.

## Support

- **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/dailydevops/pulse/issues)
- **Documentation**: Read the full documentation at [https://github.com/dailydevops/pulse](https://github.com/dailydevops/pulse)

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
