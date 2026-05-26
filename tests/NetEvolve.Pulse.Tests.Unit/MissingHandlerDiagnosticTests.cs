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

/// <summary>
/// Audit Round 01 — U15: A first-time user who calls only <c>services.AddPulse()</c>
/// (no <c>AddCommandHandler</c>, no <c>AddHandlersFromCallingAssembly</c>, no
/// <c>[PulseHandler]</c>) gets the stock DI message <i>"No service for type
/// 'ICommandHandler&lt;...&gt;' has been registered"</i>. This message lacks any
/// actionable Pulse-specific guidance. The test asserts the exception message mentions
/// at least one of "scan", "PulseHandler", "AddCommandHandler", or "SourceGeneration".
/// </summary>
[TestGroup("U15")]
public sealed class MissingHandlerDiagnosticTests
{
    [Test]
    public async Task SendAsync_With_No_Handler_Registered_Should_Mention_Scanning_Or_PulseHandler_Or_AddCommandHandler()
    {
        // Arrange — minimal Pulse setup, NO handler registration of any kind.
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var scope = provider.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                // Act + Assert — capture the exception thrown by SendAsync.
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await mediator.SendAsync(new MissingHandlerSomeCommand()).ConfigureAwait(false)
                );

                var msg = ex?.Message ?? string.Empty;

                var mentionsScanning =
                    msg.Contains("scan", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("assembly", StringComparison.OrdinalIgnoreCase);
                var mentionsPulseHandler = msg.Contains("PulseHandler", StringComparison.OrdinalIgnoreCase);
                var mentionsAddCommandHandler = msg.Contains("AddCommandHandler", StringComparison.OrdinalIgnoreCase);
                var mentionsSourceGen =
                    msg.Contains("SourceGeneration", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("source generator", StringComparison.OrdinalIgnoreCase);

                var actionable =
                    mentionsScanning || mentionsPulseHandler || mentionsAddCommandHandler || mentionsSourceGen;

                _ = await Assert
                    .That(actionable)
                    .IsTrue()
                    .Because(
                        "U15: Missing-handler error must hint at remediation "
                            + "(assembly scanning, [PulseHandler], AddCommandHandler<>, or SourceGeneration). "
                            + $"Got: \"{msg}\""
                    );
            }
        }
    }
}

internal sealed record MissingHandlerSomeCommand : ICommand
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}
