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

The transport uses `ITopicNameResolver` to determine the destination queue or topic name for each outbox message. By default, the `DefaultTopicNameResolver` returns the `Type.Name` of `OutboxMessage.EventType`, which is a `System.Type` (e.g., an event of type `MyApp.Events.OrderCreated` resolves to `"OrderCreated"`).

You can provide a custom resolver to implement different routing strategies:

```csharp
public class CustomTopicNameResolver : ITopicNameResolver
{
    public string Resolve(OutboxMessage message)
    {
        // Route based on event type, metadata, or other logic
        return message.EventType.Name.Contains("Order", StringComparison.Ordinal) ? "orders-topic" : "events-topic";
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
| `EnableBatching` | Enables batch sending per outbox batch. Messages are grouped by resolved topic name for efficient batching. Defaults to `true`. See "Partitioned Entities with Duplicate Detection" below. |

### Partitioned Entities with Duplicate Detection

If a queue or topic is partitioned and has duplicate detection enabled, Service Bus uses the `MessageId` as the partition key. In a partitioned Premium namespace every entity is partitioned, but duplicate detection is still set per entity. Each outbox message has its own `MessageId`, and the service rejects a batch whose messages have different partition keys ([Microsoft Learn](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-partitioning#use-of-partition-keys)).

When the service rejects a batch for this reason, the transport sends the messages of that batch one at a time. Every later message for that entity is also sent individually, for as long as the transport lives. No message is lost, and each message keeps its own `MessageId`. This costs one rejected request per entity. To avoid it, set `EnableBatching = false` for namespaces with such entities:

```csharp
services.AddPulse(config => config.UseAzureServiceBusTransport(options =>
{
    options.FullyQualifiedNamespace = "contoso.servicebus.windows.net";
    options.EnableBatching = false;
}));
```

## Health Checks

`AzureServiceBusMessageTransport.IsHealthyAsync` checks if the Service Bus client is operational by verifying the client is not closed. This only inspects the local state of the client and does **not** verify actual network connectivity to Azure Service Bus. Operators should be aware that a healthy state here does not guarantee remote connectivity.
