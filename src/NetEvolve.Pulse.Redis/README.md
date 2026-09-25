# NetEvolve.Pulse.Redis

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.Redis.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Redis/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.Redis.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Redis/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Redis idempotency provider for Pulse using `StackExchange.Redis`. Implements `IIdempotencyKeyRepository` with atomic `SET NX` operations (with expiry) for high-throughput, distributed idempotency enforcement without read-before-write round-trips.

## Features

- Atomic `SET key value NX` with expiry — single round-trip, no race conditions
- Keys namespaced as `{Schema}:{TableName}:{idempotencyKey}` (default `pulse:IdempotencyKey:{idempotencyKey}`)
- Logical TTL evaluated through `TimeProvider`, plus a physical Redis expiry for automatic cleanup
- Startup validation via `ValidateOnStart()`
- Requires `IConnectionMultiplexer` registered by the caller; the multiplexer's default database is used

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.Redis
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.Redis
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.Redis" />
```

## Quick Start

```csharp
// 1. Register IConnectionMultiplexer (StackExchange.Redis)
services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect("localhost:6379"));

// 2. Register the Redis idempotency store
services.AddPulse(config => config
    .AddRedisIdempotencyStore()
);
```

## Configuration

Configure the shared `IdempotencyKeyOptions` via code:

```csharp
services.AddPulse(config => config
    .AddRedisIdempotencyStore(opts =>
    {
        opts.Schema = "myapp";
        opts.TableName = "idempotency";
        opts.TimeToLive = TimeSpan.FromHours(48);
    })
);
```

No configuration section is bound automatically. To use `appsettings.json`, bind the options yourself, for example
`services.Configure<IdempotencyKeyOptions>(configuration.GetSection("MySection"))`.

## Options

| Property | Default | Description |
|---|---|---|
| `Schema` | `"pulse"` | First segment of the Redis key. `null` or empty yields an empty segment (`:IdempotencyKey:{key}`). |
| `TableName` | `"IdempotencyKey"` | Second segment of the Redis key. Must not be `null`, empty, or whitespace. |
| `TimeToLive` | `null` | Logical expiry. When `null`, keys never expire logically. When set, it must be greater than zero and at most `TimeSpan.MaxValue` minus one hour (the physical expiry adds one hour). |

The physical Redis expiry of each key is `TimeToLive` plus one hour, or 24 hours when `TimeToLive` is `null` (so with a `null` TTL, keys are still evicted by Redis after 24 hours).
Invalid options cause an `OptionsValidationException` at startup or on first resolution of the options.
