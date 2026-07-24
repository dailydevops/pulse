# NetEvolve.Pulse.AzureQueueStorage

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.AzureQueueStorage.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AzureQueueStorage/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.AzureQueueStorage.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.AzureQueueStorage/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Azure Queue Storage transport for the Pulse outbox pattern. A cost-effective alternative to Azure Service Bus, available in every Azure Storage account with a 48 KB raw message size limit.

## Features

- **Connection Flexibility**: Use a connection string or `DefaultAzureCredential` with a service URI.
- **Automatic Queue Creation**: Optionally creates the target queue on first use via `CreateQueueIfNotExists`.
- **Base64 Encoding**: Messages are JSON-serialized and Base64-encoded before sending, matching Azure Queue Storage requirements.
- **Size Guard**: Throws `InvalidOperationException` when a raw message exceeds 48 KB.
- **Sequential Batch Delivery**: `SendBatchAsync` iterates messages sequentially.
- **Dependency Injection**: Single call `UseAzureQueueStorageTransport` wires the transport.

## Installation

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.AzureQueueStorage
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.AzureQueueStorage" Version="x.x.x" />
```

## Quick Start

### Connection String

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;

var services = new ServiceCollection();

services.AddPulse(config => config.UseAzureQueueStorageTransport(
    connectionString: builder.Configuration["Storage:ConnectionString"]!
));
```

### Managed Identity

```csharp
services.AddPulse(config => config.UseAzureQueueStorageTransport(
    queueServiceUri: new Uri("https://myaccount.queue.core.windows.net")
));
```

## Configuration

| Option | Description |
|---|---|
| `ConnectionString` | Azure Storage connection string. Required when `QueueServiceUri` is not set. |
| `QueueServiceUri` | Azure Queue Storage service URI used with managed identity (`DefaultAzureCredential`). Required when `ConnectionString` is not set. |
| `QueueName` | Name of the queue to send messages to. Defaults to `pulse-outbox`. |
| `MessageVisibilityTimeout` | Optional visibility timeout applied to each sent message. Defaults to the queue's default. |
| `CreateQueueIfNotExists` | Automatically creates the queue on first use. Defaults to `true`. |

## Configuration via appsettings.json

Register `AzureQueueStorageTransportOptionsConfiguration` to bind from the `Pulse:Transports:AzureQueueStorage` section:

```csharp
services.AddSingleton<IConfigureOptions<AzureQueueStorageTransportOptions>, AzureQueueStorageTransportOptionsConfiguration>();
```

```json
{
  "Pulse": {
    "Transports": {
      "AzureQueueStorage": {
        "QueueName": "my-outbox",
        "CreateQueueIfNotExists": true
      }
    }
  }
}
```
