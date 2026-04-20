# NetEvolve.Pulse.Redis

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.Redis.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Redis/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.Redis.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Redis/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Redis idempotency provider for Pulse using `StackExchange.Redis`. Implements `IIdempotencyStore` with atomic `SET NX EX` operations for high-throughput, distributed idempotency enforcement without read-before-write round-trips.

## Features

- Atomic `SET key value EX ttl NX` — single round-trip, no race conditions
- Configurable key prefix, TTL, and Redis database index
- Automatic configuration binding from `Pulse:Idempotency:Redis` section
- Startup validation via `ValidateOnStart()`
- Requires `IConnectionMultiplexer` registered by the caller

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

Bind options from `appsettings.json`:

```json
{
  "Pulse": {
    "Idempotency": {
      "Redis": {
        "KeyPrefix": "myapp:idempotency:",
        "TimeToLive": "24:00:00",
        "Database": -1
      }
    }
  }
}
```

Or configure via code:

```csharp
services.AddPulse(config => config
    .AddRedisIdempotencyStore(opts =>
    {
        opts.KeyPrefix = "myapp:idempotency:";
        opts.TimeToLive = TimeSpan.FromHours(48);
        opts.Database = 0;
    })
);
```

## Options

| Property | Default | Description |
|---|---|---|
| `KeyPrefix` | `"pulse:idempotency:"` | Prefix applied to all idempotency keys in Redis |
| `TimeToLive` | `TimeSpan.FromHours(24)` | Expiry duration for stored keys |
| `Database` | `-1` | Redis database index (`-1` = default) |
