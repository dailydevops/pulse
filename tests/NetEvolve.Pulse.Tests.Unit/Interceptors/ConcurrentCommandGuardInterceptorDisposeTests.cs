namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Additional invariants for <see cref="ConcurrentCommandGuardInterceptor{TRequest, TResponse}"/>:
/// disposal is idempotent and semaphore acquisition still works as long as the interceptor has not
/// been disposed.
/// </summary>
[TestGroup("Interceptors")]
public sealed class ConcurrentCommandGuardInterceptorDisposeTests
{
    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        var interceptor = new ConcurrentCommandGuardInterceptor<ExclusiveCommand, string>();

        interceptor.Dispose();
        interceptor.Dispose();

        // No exception expected — Dispose must be idempotent.
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Dispose_AfterUse_DoesNotThrow(CancellationToken cancellationToken)
    {
        var interceptor = new ConcurrentCommandGuardInterceptor<ExclusiveCommand, string>();

        // Use the interceptor first to populate the internal dictionary
        _ = await interceptor
            .HandleAsync(new ExclusiveCommand(), (_, _) => Task.FromResult("ok"), cancellationToken)
            .ConfigureAwait(false);

        interceptor.Dispose();
        interceptor.Dispose();

        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Dispose_WhileHandlerInFlight_DoesNotReplaceCommandOutcome(CancellationToken cancellationToken)
    {
        var interceptor = new ConcurrentCommandGuardInterceptor<ExclusiveCommand, string>();
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var inFlightCall = interceptor.HandleAsync(
            new ExclusiveCommand(),
            async (_, _) =>
            {
                handlerEntered.SetResult();
#pragma warning disable VSTHRD003 // TaskCompletionSource gate is signaled by the test, not started here
                await releaseHandler.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
                return "real-result";
            },
            cancellationToken
        );

        await handlerEntered.Task.ConfigureAwait(false);

        // The DI container disposes the singleton interceptor while the command still executes.
        interceptor.Dispose();

        releaseHandler.SetResult();

        // The command's real outcome must not be replaced by an ObjectDisposedException
        // thrown when the finally block releases the disposed semaphore.
        var result = await inFlightCall.ConfigureAwait(false);
        _ = await Assert.That(result).IsEqualTo("real-result");
    }

    private sealed record ExclusiveCommand : IExclusiveCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
