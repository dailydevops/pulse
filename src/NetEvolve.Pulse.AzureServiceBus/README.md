# NetEvolve.Pulse.AzureServiceBus

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.AzureServiceBus.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AzureServiceBus/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.AzureServiceBus.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AzureServiceBus/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Azure Service Bus transport for the Pulse outbox pattern. Publishes outbox messages directly to Azure Service Bus queues or topics via the `Azure.Messaging.ServiceBus` SDK, enabling reliable event delivery without requiring Dapr.

## Features

- **Direct Service Bus SDK**: Publish outbox messages to Azure Service Bus queues or topics without the Dapr sidecar
- **Managed Identity**: Authenticate with `DefaultAzureCredential` when no connection string is provided
- **Batch sending**: Use `ServiceBusMessageBatch` for efficient bulk delivery
- **Health checks**: Verify Service Bus connectivity via `ServiceBusAdministrationClient`
- **Configurable**: Choose between connection string and Managed Identity authentication

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.AzureServiceBus
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.AzureServiceBus
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.AzureServiceBus" Version="x.x.x" />
```

## Quick Start

### Register services with a connection string

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;

var services = new ServiceCollection();

services.AddPulse(config => config
    .AddOutbox()
    .UseAzureServiceBusTransport(options =>
    {
        options.ConnectionString = "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=...";
        options.EntityPath = "my-queue";
        options.EnableBatching = true;
    }));
```

### Register services with Managed Identity

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .UseAzureServiceBusTransport(options =>
    {
        options.FullyQualifiedNamespace = "mynamespace.servicebus.windows.net";
        options.EntityPath = "my-topic";
    }));
```

## Configuration

### `AzureServiceBusTransportOptions`

| Property                | Type      | Default  | Description                                                                 |
| ----------------------- | --------- | -------- | --------------------------------------------------------------------------- |
| `ConnectionString`      | `string?` | `null`   | Service Bus connection string. Takes precedence over `FullyQualifiedNamespace`. |
| `FullyQualifiedNamespace` | `string?` | `null` | Fully qualified namespace (e.g. `mynamespace.servicebus.windows.net`). Used with `DefaultAzureCredential`. |
| `EntityPath`            | `string`  | `""`     | Target queue or topic name.                                                 |
| `EnableBatching`        | `bool`    | `true`   | Use `ServiceBusMessageBatch` for `SendBatchAsync`.                          |

## How It Works

1. Your application stores events in the outbox via `IEventOutbox.StoreAsync` within a database transaction.
2. The Pulse background processor polls the outbox for pending messages.
3. For each message, `AzureServiceBusMessageTransport` serializes the payload and calls `ServiceBusSender.SendMessageAsync` (or batch equivalent).
4. On success, the message is marked as processed; on failure, it remains pending for the next poll cycle.

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- Azure Service Bus namespace
- `Azure.Messaging.ServiceBus` SDK
- `Azure.Identity` for Managed Identity support

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core mediator and outbox abstractions
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Core contracts and abstractions
- [**NetEvolve.Pulse.EntityFramework**](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework Core persistence provider
- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) - SQL Server ADO.NET persistence provider
- [**NetEvolve.Pulse.Dapr**](https://www.nuget.org/packages/NetEvolve.Pulse.Dapr/) - Dapr pub/sub transport

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
