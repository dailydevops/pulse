# NetEvolve.Pulse.AzureServiceBus

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.AzureServiceBus.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AzureServiceBus/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.AzureServiceBus.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AzureServiceBus/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Native Azure Service Bus transport for Pulse outbox delivery with dynamic topic routing, batching, managed identity, and built-in health checks.

## Features

- **Dynamic Topic Routing**: Route outbox events to different queues or topics using `ITopicNameResolver` based on message content or metadata.
- **Connection Flexibility**: Use a connection string or `DefaultAzureCredential` with a fully qualified namespace.
- **Batching**: Toggle batch sending per outbox batch to reduce broker calls. Messages are automatically grouped by resolved topic name for efficient batching.
- **Health Checks**: Reports transport availability by checking the Service Bus client's local closed/open state (does not verify network connectivity to Azure Service Bus).
- **Dependency Injection**: Single call `UseAzureServiceBusTransport` wires the Service Bus client and transport.
- **Env/Emulator Friendly**: Works with Azure-hosted namespaces, dev tunnels, or local emulator connection strings.

## Installation

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.AzureServiceBus
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.AzureServiceBus" Version="x.x.x" />
```

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;

var services = new ServiceCollection();

services.AddPulse(config => config.UseAzureServiceBusTransport(options =>
{
    options.ConnectionString = builder.Configuration["ServiceBus:ConnectionString"];
    options.EnableBatching = true;
}));
```

### Managed Identity Example

```csharp
services.AddPulse(config => config.UseAzureServiceBusTransport(options =>
{
    options.FullyQualifiedNamespace = "contoso.servicebus.windows.net";
}));
```

## Topic Name Resolution

The transport uses `ITopicNameResolver` to determine the destination queue or topic name for each outbox message. By default, the `DefaultTopicNameResolver` extracts the simple class name from the event type (e.g., `"MyApp.Events.OrderCreated"` → `"OrderCreated"`).

You can provide a custom resolver to implement different routing strategies:

```csharp
public class CustomTopicNameResolver : ITopicNameResolver
{
    public string Resolve(OutboxMessage message)
    {
        // Route based on event type, metadata, or other logic
        return message.EventType.Contains("Order") ? "orders-topic" : "events-topic";
    }
}

// Register the custom resolver
services.AddSingleton<ITopicNameResolver, CustomTopicNameResolver>();
```

## Configuration

| Option | Description |
|---|---|
| `ConnectionString` | Connection string for the Service Bus namespace. Required when `FullyQualifiedNamespace` is not set. |
| `FullyQualifiedNamespace` | FQDN (e.g., `contoso.servicebus.windows.net`) used with managed identity (`DefaultAzureCredential`). |
| `EnableBatching` | Enables batch sending per outbox batch. Messages are grouped by resolved topic name for efficient batching. Defaults to `true`. |

## Health Checks

`AzureServiceBusMessageTransport.IsHealthyAsync` checks if the Service Bus client is operational by verifying the client is not closed. This only inspects the local state of the client and does **not** verify actual network connectivity to Azure Service Bus. Operators should be aware that a healthy state here does not guarantee remote connectivity.
