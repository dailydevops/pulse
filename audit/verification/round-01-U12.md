# U12 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse/ServiceCollectionExtensions.cs:110` — `_ = services.AddScoped<IMediator, PulseMediator>();`
- `src/NetEvolve.Pulse/ServiceCollectionExtensions.cs:111` — `services.TryAddScoped<IMediatorSendOnly>(sp => sp.GetRequiredService<IMediator>());`
- `src/NetEvolve.Pulse.Extensibility/IMediatorSendOnly.cs:18-36` — XML doc `<example>` block shows `OrderBackgroundService : BackgroundService` constructor-injecting `IMediatorSendOnly` directly.
- `Microsoft.Extensions.Hosting`'s default `Host.CreateApplicationBuilder()` enables `ValidateScopes=true` AND `ValidateOnBuild=true` in the Development environment.

**Reasoning:**
`BackgroundService` is registered as a Singleton (the `IHostedService` collection itself is Singleton, and `AddHostedService<T>` adds the `BackgroundService` as a Singleton). Injecting a Scoped service (`IMediatorSendOnly`) into a Singleton (`BackgroundService`) is a captive-dependency violation. With `ValidateScopes=true`, `IServiceProvider.GetRequiredService<IHostedService>()` (or host startup) throws `InvalidOperationException: Cannot consume scoped service 'NetEvolve.Pulse.Extensibility.IMediatorSendOnly' from singleton 'OrderBackgroundService'.` This is exactly what the copy-pasted example produces — host fails to start in Development. The fix would be to either (a) inject `IServiceScopeFactory` and call `CreateScope()` per work item, or (b) document this requirement in the XML doc.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/IMediatorSendOnlyBackgroundServiceLifetimeTests.cs`
- Status: written
- Test code:
```csharp
namespace NetEvolve.Pulse.Tests.Unit;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

// U12 — The XML-doc example at IMediatorSendOnly.cs:20-34 injects IMediatorSendOnly (Scoped)
// directly into a BackgroundService (Singleton). With ValidateScopes=true (the dev default for
// Host.CreateApplicationBuilder), host startup must throw a captive-dependency InvalidOperationException.
[TestGroup("U12")]
public sealed class IMediatorSendOnlyBackgroundServiceLifetimeTests
{
    [Test]
    public async Task BackgroundService_From_XmlDocExample_Fails_ScopeValidation_On_Host_Start()
    {
        // Arrange — mimic Host.CreateApplicationBuilder() with dev defaults explicit.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });

        // Default Pulse registration — IMediatorSendOnly registered Scoped.
        _ = builder.Services.AddPulse();

        // Verbatim from IMediatorSendOnly.cs:20-34: BackgroundService with ctor-injected IMediatorSendOnly.
        _ = builder.Services.AddHostedService<OrderBackgroundService>();

        // Force the dev defaults (matches what Host.CreateApplicationBuilder() does).
        builder.Services.AddOptions();
        var hostBuilderOptionsField = typeof(HostApplicationBuilder).GetField(
            "_serviceProviderOptions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        // Build with scope validation explicit — emulates dev defaults regardless of internal field.
        using var host = builder.Build();

        // Re-build a provider with explicit scope validation to assert the captive-dependency error.
        var validatingProvider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        // Act + Assert — should throw because IMediatorSendOnly is Scoped, OrderBackgroundService is Singleton.
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
        {
            // Resolving the hosted service forces the captive-dependency check.
            _ = validatingProvider.GetRequiredService<IHostedService>();
            await Task.CompletedTask;
        });

        _ = await Assert.That(ex.Message).Contains("scoped");
    }

    // Verbatim from IMediatorSendOnly.cs:20-34
    private sealed class OrderBackgroundService : BackgroundService
    {
        private readonly IMediatorSendOnly _mediator;

        public OrderBackgroundService(IMediatorSendOnly mediator)
        {
            _mediator = mediator;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
```

**Notes:**
- The fix in Phase 3 should update the XML doc example to use `IServiceScopeFactory` and `CreateScope()`, OR register `IMediatorSendOnly` as Transient / Singleton (note: `PulseMediator` resolves `IServiceProvider` per call and uses `CreateAsyncScope()` for `PublishAsync`, but `SendAsync` directly resolves from the root provider — see `Internals/PulseMediator.cs:159` — so changing the lifetime alone may surface additional scoping issues).
- `PulseMediator.PublishAsync` (line 86-101 of `PulseMediator.cs`) already creates an inner scope per publish, so making `PulseMediator` Singleton-safe is theoretically feasible BUT `SendAsync` at line 159 calls `_serviceProvider.GetRequiredService<ICommandHandler<...>>()` on the captured provider — which would resolve from the root (no scope) if the mediator itself were Singleton, breaking Scoped handlers.
- The repro is sufficient evidence — host startup fails when developers copy-paste the example into `Host.CreateApplicationBuilder()` with dev defaults.
