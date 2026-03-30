# NetEvolve.Pulse.SQLite

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.SQLite.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SQLite/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.SQLite.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SQLite/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

SQLite persistence provider for the Pulse outbox pattern using plain ADO.NET. Enables the outbox pattern with zero infrastructure dependencies — ideal for embedded applications, CLI tools, IoT/edge services, and local development environments.

## Features

- **Zero Infrastructure**: Single-file embedded database, no server required
- **Plain ADO.NET**: Direct SQLite access via `Microsoft.Data.Sqlite`
- **Concurrent Safety**: Uses `BEGIN IMMEDIATE` transactions to prevent duplicate message pickup
- **WAL Mode**: Optional Write-Ahead Logging for concurrent read access during writes
- **Dead Letter Management**: Built-in support for inspecting, replaying, and monitoring dead-letter messages via `IOutboxManagement`
- **Configurable Table Name**: Customize the table name for your deployment
- **In-Memory Support**: Use `Data Source=:memory:` for testing scenarios without file I/O

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.SQLite
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.SQLite
```

## Prerequisites

Execute the DDL migration script `Scripts/001_CreateOutboxTable.sql` once against your SQLite database before starting the application:

```bash
sqlite3 outbox.db < 001_CreateOutboxTable.sql
```

## Usage

### Basic Setup

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .UseSQLiteOutbox("Data Source=outbox.db")
);
```

### Custom Options

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .UseSQLiteOutbox(opts =>
    {
        opts.ConnectionString = "Data Source=outbox.db";
        opts.TableName = "OutboxMessage";
        opts.EnableWalMode = true;
    })
);
```

### Testing with In-Memory Database

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .UseSQLiteOutbox(opts =>
    {
        opts.ConnectionString = $"Data Source=testdb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        opts.EnableWalMode = false; // WAL mode not supported for in-memory databases
    })
);
```

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ConnectionString` | `string` | `"Data Source=outbox.db"` | SQLite connection string |
| `TableName` | `string` | `"OutboxMessage"` | Table name for outbox messages |
| `EnableWalMode` | `bool` | `true` | Enable WAL journal mode for concurrent reads |
| `JsonSerializerOptions` | `JsonSerializerOptions?` | `null` | JSON options for event serialization |

## License

This project is licensed under the [MIT License](https://github.com/dailydevops/pulse/blob/main/LICENSE).
