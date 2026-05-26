# U15 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse/ServiceCollectionExtensions.cs:88-114` — `AddPulse()` registers `PulseMediator` and friends but performs no handler discovery.
- `src/NetEvolve.Pulse/Internals/PulseMediator.cs:159` — `var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();`
- `src/NetEvolve.Pulse/Internals/PulseMediator.cs:118` (queries) and `:139` (stream queries) — same pattern.
- The thrown exception is whatever `GetRequiredService<T>()` produces from `Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions`. There is no try/catch, no wrapping, no diagnostic enrichment in `PulseMediator`.

**Reasoning:**
A first-time user who calls only `services.AddPulse()` (no `AddCommandHandler`, no `AddHandlersFromCallingAssembly`, no source-generator registration) gets a stock DI message such as `System.InvalidOperationException: No service for type 'NetEvolve.Pulse.Extensibility.ICommandHandler\`2[...]' has been registered.` This message does not mention assembly scanning, the `[PulseHandler]` attribute, the source generator package (`NetEvolve.Pulse.SourceGeneration`), or even Pulse-specific terminology. Compared to MediatR's one-liner `services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>())`, Pulse forces the user to discover one of three registration paths (manual `AddCommandHandler<>`, AOT-incompatible `AddHandlersFromCallingAssembly`, or source-gen) without any in-product hint. The test below asserts the exception message DOES mention at least one Pulse-specific hint; today that assertion fails because the message is the generic DI string.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/MissingHandlerDiagnosticTests.cs`
- Status: written
- Test code:
```csharp
namespace NetEvolve.Pulse.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

// U15 — services.AddPulse() with no further calls + mediator.SendAsync(new SomeCommand())
// today throws a generic "No service for type 'ICommandHandler<...>'" error. The exception
// message must mention at least one of: "scan", "PulseHandler", "AddCommandHandler", or
// "SourceGeneration", to give the developer an actionable next step.
[TestGroup("U15")]
public sealed class MissingHandlerDiagnosticTests
{
    [Test]
    public async Task SendAsync_With_No_Handler_Registered_Should_Mention_Scanning_Or_PulseHandler_Or_AddCommandHandler()
    {
        // Arrange — minimal Pulse setup, no handler registration of any kind.
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Act + Assert — capture the exception thrown by SendAsync.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync(new SomeCommand()).ConfigureAwait(false)
        );

        var msg = ex.Message ?? string.Empty;

        var mentionsScanning = msg.Contains("scan", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("assembly", StringComparison.OrdinalIgnoreCase);
        var mentionsPulseHandler = msg.Contains("PulseHandler", StringComparison.OrdinalIgnoreCase);
        var mentionsAddCommandHandler = msg.Contains("AddCommandHandler", StringComparison.OrdinalIgnoreCase);
        var mentionsSourceGen = msg.Contains("SourceGeneration", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("source generator", StringComparison.OrdinalIgnoreCase);

        var actionable = mentionsScanning || mentionsPulseHandler || mentionsAddCommandHandler || mentionsSourceGen;

        _ = await Assert
            .That(actionable)
            .IsTrue()
            .Because(
                "U15: Missing-handler error must hint at remediation. "
                    + $"Got: \"{msg}\""
            );
    }

    public sealed record SomeCommand : ICommand;
}
```

**Notes:**
- The test passes today only if Phase 3 wraps the missing-handler case in `PulseMediator.SendAsync` / `QueryAsync` / `StreamQueryAsync` with a Pulse-specific message. A clean implementation: replace `GetRequiredService<...>()` with `GetService<...>()` and throw `PulseMediatorException` (or `InvalidOperationException`) carrying a message like *"No ICommandHandler<TCommand, TResponse> registered. Register one via services.AddCommandHandler<TCommand, THandler>(), services.AddHandlersFromCallingAssembly(), or by decorating handlers with [PulseHandler] and referencing NetEvolve.Pulse.SourceGeneration."*
- Same diagnostic gap exists for queries (line 118) and stream queries (line 139). Phase 3 should fix all three.
- The `ICommand` interface lives at `src/NetEvolve.Pulse.Extensibility/ICommand.cs` (verified — `public interface ICommand : ICommand<Void>;`), so `SomeCommand : ICommand` is the canonical "no response" form used by the IMediator overload `SendAsync<TCommand>` (line 87-88 of `IMediatorSendOnly.cs`).
