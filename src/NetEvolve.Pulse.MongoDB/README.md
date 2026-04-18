# NetEvolve.Pulse.MongoDB

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.MongoDB.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.MongoDB/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.MongoDB.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.MongoDB/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

MongoDB persistence provider for the Pulse outbox pattern using the official MongoDB C# driver. Designed for document-oriented architectures where the outbox participates in the same MongoDB database as domain aggregates.

## Features

- Native MongoDB implementation using `MongoDB.Driver`
- Atomic message claiming via `FindOneAndUpdateAsync` with `CreatedAt` sort to prevent concurrent duplicates
- Configurable database name and collection name (default: `outbox_messages`)
- Health check support via MongoDB `ping` command
- Requires `IMongoClient` to be registered in the dependency injection container by the caller

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.MongoDB
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.MongoDB
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.MongoDB" />
```

## Quick Start

```csharp
using MongoDB.Driver;
using NetEvolve.Pulse;

// Register IMongoClient in DI (required by the caller)
services.AddSingleton<IMongoClient>(new MongoClient("mongodb://localhost:27017"));

services.AddPulse(config => config
    .AddMongoDbOutbox(opts =>
    {
        opts.DatabaseName = "myapp";
        opts.CollectionName = "outbox_messages";
    }));
```

## Usage

### Using `AddMongoDbOutbox` (Full Setup)

Registers core outbox services and the MongoDB repository in a single call.

```csharp
services.AddSingleton<IMongoClient>(new MongoClient(connectionString));

services.AddPulse(config => config
    .AddMongoDbOutbox(opts =>
    {
        opts.DatabaseName = "myapp";
    }));
```

### Using `UseMongoDbOutbox` (Provider Swap)

Call `AddOutbox()` first, then `UseMongoDbOutbox` to set the MongoDB repository.

```csharp
services.AddSingleton<IMongoClient>(new MongoClient(connectionString));

services.AddPulse(config => config
    .AddOutbox()
    .UseMongoDbOutbox(opts =>
    {
        opts.DatabaseName = "myapp";
        opts.CollectionName = "outbox_messages";
    }));
```

## Configuration

| Property | Type | Default | Description |
|---|---|---|---|
| `DatabaseName` | `string` | _(required)_ | The MongoDB database name |
| `CollectionName` | `string` | `outbox_messages` | The MongoDB collection name |

## Requirements

- .NET 8.0 or higher (net8.0, net9.0, net10.0 targets)
- `IMongoClient` registered in the dependency injection container

## Related Packages

- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) – SQL Server outbox provider
- [**NetEvolve.Pulse.PostgreSql**](https://www.nuget.org/packages/NetEvolve.Pulse.PostgreSql/) – PostgreSQL outbox provider
- [**NetEvolve.Pulse.SQLite**](https://www.nuget.org/packages/NetEvolve.Pulse.SQLite/) – SQLite outbox provider
- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) – Core Pulse mediator and abstractions

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
> Visit us at [https://www.daily-devops.net](https://www.daily-devops.net) for more information about our services and solutions.
