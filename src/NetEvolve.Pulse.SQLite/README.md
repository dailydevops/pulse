# NetEvolve.Pulse.SQLite

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.SQLite.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SQLite/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.SQLite.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SQLite/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

SQLite persistence provider for the Pulse outbox pattern using plain ADO.NET. Designed for embedded, edge, and CLI scenarios where you need outbox reliability without external infrastructure.

## Features

- Embedded, single-file storage with no server dependency
- ADO.NET implementation using `Microsoft.Data.Sqlite`
- Safe concurrent polling via `BEGIN IMMEDIATE` transactions
- Optional Write-Ahead Logging (WAL) for read/write concurrency
- Outbox management APIs for inspecting, replaying, and cleaning messages
- Configurable table name and connection string (file or in-memory)

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.SQLite
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.SQLite
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.SQLite" />
```

## Quick Start

```csharp
using NetEvolve.Pulse;
using NetEvolve.Pulse.SQLite;

services.AddPulse(config => config
    .AddOutbox()
    .UseSQLiteOutbox("Data Source=outbox.db"));
```

## Usage

### Basic Example

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .UseSQLiteOutbox(opts =>
    {
        opts.ConnectionString = "Data Source=outbox.db";
        opts.EnableWalMode = true;
    }));
```

### Advanced Example (In-Memory / Custom Table)

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .UseSQLiteOutbox(opts =>
    {
        opts.ConnectionString = $"Data Source=testdb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        opts.TableName = "OutboxMessage";
        opts.EnableWalMode = false; // WAL unsupported for in-memory
    }));
```

## Configuration

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .UseSQLiteOutbox(opts =>
    {
        opts.ConnectionString = "Data Source=outbox.db";
        opts.TableName = "OutboxMessage";
        opts.EnableWalMode = true;
        opts.JsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }));
```

## Requirements

- .NET 8.0 or higher (net8.0, net9.0, net10.0 targets)
- SQLite database file access or in-memory connection string

## Related Packages

- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) – SQL Server outbox provider
- [**NetEvolve.Pulse.PostgreSql**](https://www.nuget.org/packages/NetEvolve.Pulse.PostgreSql/) – PostgreSQL outbox provider
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
