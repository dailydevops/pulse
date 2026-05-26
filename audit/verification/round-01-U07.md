# U07 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse.AzureServiceBus/README.md:32-45` (quick-start: `services.AddPulse(config => config.UseAzureServiceBusTransport(options => …))` — no `AddOutbox`, no persistence provider).
- `src/NetEvolve.Pulse.AzureServiceBus/AzureServiceBusExtensions.cs:26-60` (`UseAzureServiceBusTransport` only adds `AzureServiceBusTransportOptions`, `ServiceBusClient`, `TokenCredential`, and replaces `IMessageTransport` — does **not** call `AddOutbox()` or register `IOutboxRepository` / `OutboxProcessorHostedService`).
- `src/NetEvolve.Pulse/OutboxExtensions.cs:39-76` (`AddOutbox` is the only path that registers `OutboxProcessorHostedService` and `IMessageTransport`; `IOutboxRepository` must come from a persistence provider).
- Counter-example: `src/NetEvolve.Pulse.SqlServer/SqlServerExtensions.cs:194-205` — `RegisterSqlServerOutboxServices` calls `AddOutbox()` and registers `IOutboxRepository` correctly.

**Reasoning:** Following the ASB quick-start verbatim builds a service collection where (a) `AddPulse` has run, (b) `IMessageTransport` is replaced with `AzureServiceBusMessageTransport`, but (c) no `OutboxProcessorHostedService` is registered (only `AddOutbox` adds it via `TryAddEnumerable`), and (d) no `IOutboxRepository` is registered. `mediator.PublishAsync(...)` will not dispatch via the outbox at all (the `OutboxEventHandler<>` is not registered either, since that is also only set up by `AddOutbox`). The DI failure surface is a silent no-op (events queue nowhere) rather than a clear, actionable error pointing the user at the missing `AddOutbox` / `AddXxxOutbox` calls.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/AzureServiceBus/AzureServiceBusReadmeQuickStartTests.cs`
- Status: written

```csharp
namespace NetEvolve.Pulse.Tests.Unit.AzureServiceBus;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Phase 2 audit U07: Building a service collection that follows
/// <c>src/NetEvolve.Pulse.AzureServiceBus/README.md:32-45</c> verbatim must surface
/// a clear, actionable error — pointing the user at the missing <c>AddOutbox()</c>
/// / persistence provider call — rather than silently leaving the outbox half-wired.
///
/// Currently FAILS: the provider builds without error, <c>IOutboxRepository</c> resolution
/// throws a generic <c>"No service for type … IOutboxRepository …"</c> message that does not
/// mention <c>AddOutbox</c>, <c>AddSqlServerOutbox</c>, or any actionable remediation.
/// </summary>
[TestGroup("AzureServiceBus")]
public sealed class AzureServiceBusReadmeQuickStartTests
{
    private const string FakeConnectionString =
        "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Fake=";

    [Test]
    public async Task AsbReadmeQuickStart_should_register_IOutboxRepository_or_throw_actionable_DI_error()
    {
        // ARRANGE — Verbatim transcription of README:32-45 quick-start.
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureServiceBusTransport(options =>
            {
                options.ConnectionString = FakeConnectionString;
                options.EnableBatching = true;
            })
        );

        // ACT — Build the provider and try to resolve the persistence side.
        await using var provider = services.BuildServiceProvider();

        // ASSERT (1) — Either IOutboxRepository is registered (preferred), or
        // resolving it throws an exception that *mentions* the missing call.
        Exception? repositoryError = null;
        try
        {
            _ = provider.GetRequiredService<IOutboxRepository>();
        }
        catch (Exception ex)
        {
            repositoryError = ex;
        }

        if (repositoryError is not null)
        {
            _ = await Assert
                .That(repositoryError.Message)
                .Contains("AddOutbox", StringComparison.OrdinalIgnoreCase)
                .Or.Contains("persistence", StringComparison.OrdinalIgnoreCase)
                .Or.Contains("provider", StringComparison.OrdinalIgnoreCase);
        }

        // ASSERT (2) — OutboxProcessorHostedService must be wired up so the transport
        // is actually drained; ASB-only registration without AddOutbox leaves it absent.
        var hostedServices = provider.GetServices<IHostedService>();
        var hasOutboxProcessor = false;
        foreach (var hosted in hostedServices)
        {
            if (hosted is OutboxProcessorHostedService)
            {
                hasOutboxProcessor = true;
                break;
            }
        }

        _ = await Assert.That(hasOutboxProcessor).IsTrue();
    }
}
```

**Notes:**
- The test asserts two things that *both* fail today:
  1. `IOutboxRepository` is either registered or its error message names `AddOutbox` / `persistence` / `provider`. Today the error is `"No service for type 'NetEvolve.Pulse.Extensibility.Outbox.IOutboxRepository' has been registered."` — generic, contains none of those tokens.
  2. `OutboxProcessorHostedService` is registered. Today it is not — `AddOutbox` is the only call site that adds it, and `UseAzureServiceBusTransport` does not call `AddOutbox`.
- Phase 3 fix options: (a) have every `Use*Transport` extension call `AddOutbox()` internally (mirrors `RegisterSqlServerOutboxServices`), or (b) validate at provider build / first publish that `IOutboxRepository` and `OutboxProcessorHostedService` are present, throwing an `InvalidOperationException` whose message lists the missing `Add*Outbox` calls.
- Same defect applies to `UseKafkaTransport`, `UseRabbitMqTransport`, and `UseMessageTransport<T>` — out of scope for U07 but worth a single shared fix.
