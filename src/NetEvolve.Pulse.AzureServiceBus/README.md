# NetEvolve.Pulse.AzureServiceBus

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.AzureServiceBus.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AzureServiceBus/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.AzureServiceBus.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AzureServiceBus/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Native Azure Service Bus transport for Pulse outbox delivery with batching, managed identity, and built-in health checks.

## Features

- **Queue or Topic**: Send outbox events directly to a queue or topic via the `EntityType` option.
- **Connection Flexibility**: Use a connection string or `DefaultAzureCredential` with a fully qualified namespace.
- **Batching**: Toggle batch sending per outbox batch to reduce broker calls.
- **Health Checks**: Uses Service Bus runtime properties to report entity availability.
- **Dependency Injection**: Single call `UseAzureServiceBusTransport` wires clients, sender, and health adapters.
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
    options.EntityPath = "outbox-queue";
    options.EntityType = AzureServiceBusEntityType.Queue; // or Topic
    options.EnableBatching = true;
}));
```

### Managed Identity Example

```csharp
services.AddPulse(config => config.UseAzureServiceBusTransport(options =>
{
    options.FullyQualifiedNamespace = "contoso.servicebus.windows.net";
    options.EntityPath = "outbox-topic";
    options.EntityType = AzureServiceBusEntityType.Topic;
}));
```

## Choosing Queue vs. Topic

- Use **Queue** (`EntityType = Queue`) for point-to-point delivery where a single processor handles each message.
- Use **Topic** (`EntityType = Topic`) when multiple subscriptions need the same outbox event.
- Set `EntityPath` to the queue or topic name; the transport sends to the configured entity and targets the same entity for health checks.

## Configuration

| Option | Description |
|---|---|
| `ConnectionString` | Connection string for the Service Bus namespace. Required when `FullyQualifiedNamespace` is not set. |
| `FullyQualifiedNamespace` | FQDN (e.g., `contoso.servicebus.windows.net`) used with managed identity (`DefaultAzureCredential`). |
| `EntityPath` | Queue or topic name that receives outbox messages. |
| `EntityType` | Target entity kind (`Queue` or `Topic`). Defaults to `Queue`. |
| `EnableBatching` | Enables batch sending per outbox batch. Defaults to `true`. |

## Health Checks

`AzureServiceBusMessageTransport.IsHealthyAsync` probes the configured entity type:

- For **queues**, it calls `GetQueueRuntimePropertiesAsync`.
- For **topics**, it calls `GetTopicRuntimePropertiesAsync`.

Any exception other than cancellation returns `false`, allowing the outbox processor to surface transport availability.
