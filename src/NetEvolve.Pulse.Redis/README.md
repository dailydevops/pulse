# NetEvolve.Pulse.Redis

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.Redis.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Redis/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.Redis.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Redis/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Redis idempotency provider for Pulse using `StackExchange.Redis`. Implements `IIdempotencyKeyRepository` with atomic single round-trip operations for high-throughput, distributed idempotency enforcement: `SET NX` when no `TimeToLive` is set, and a server-side Lua script that also refreshes expired keys when a `TimeToLive` is set.

## Features

- Without a `TimeToLive`: atomic `SET key value NX` in a single round-trip
- With a `TimeToLive`: one atomic Lua script (`EVAL`) that reserves an absent key, or refreshes an expired key and resets its `PX` expiry
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

The physical Redis expiry of each key is `TimeToLive` plus one hour. When `TimeToLive` is `null`, keys are stored without a Redis expiry. They are only removed if the server's `maxmemory-policy` evicts non-volatile keys (`allkeys-*`), and doing so breaks duplicate detection. Under `volatile-*` or `noeviction` policies the key space grows until the server runs out of memory. Set a `TimeToLive` if the key space must stay bounded.

When `TimeToLive` is set, reservation runs a Lua script. The Redis user must be allowed to run scripting commands (`EVAL` and `EVALSHA`, for example through the ACL category `+@scripting`). Managed tiers or ACLs that block scripting make reservation fail once a `TimeToLive` is configured.

The script compares stored timestamps as UTC round-trip text. The provider always writes UTC values. A value with a non-UTC offset, written by an earlier version through a direct `IIdempotencyKeyRepository.StoreAsync` call, is treated as present until its physical Redis expiry removes it. A value stored while `TimeToLive` was `null` has no physical expiry, so delete such keys manually if they must become reservable again.
Invalid options cause an `OptionsValidationException` at startup or on first resolution of the options.
