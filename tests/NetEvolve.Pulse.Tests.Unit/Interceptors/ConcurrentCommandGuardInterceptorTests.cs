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

[TestGroup("Interceptors")]
public sealed class ConcurrentCommandGuardInterceptorTests
{
    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        using var interceptor = new ConcurrentCommandGuardInterceptor<ExclusiveCommand, string>();
        var command = new ExclusiveCommand();

        _ = await Assert
            .That(async () => await interceptor.HandleAsync(command, null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_ExclusiveCommand_ExecutesHandler(CancellationToken cancellationToken)
    {
        using var interceptor = new ConcurrentCommandGuardInterceptor<ExclusiveCommand, string>();
        var command = new ExclusiveCommand();
        var handlerCalled = false;

        var result = await interceptor
            .HandleAsync(
                command,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.FromResult("exclusive-response");
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("exclusive-response");
            _ = await Assert.That(handlerCalled).IsTrue();
        }
    }

    [Test]
    public async Task HandleAsync_ExclusiveCommand_HandlerThrows_SemaphoreReleased(CancellationToken cancellationToken)
    {
        using var interceptor = new ConcurrentCommandGuardInterceptor<ExclusiveCommand2, string>();
        var command = new ExclusiveCommand2();

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(
                        command,
                        (_, _) => Task.FromException<string>(new InvalidOperationException("handler error")),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        // If semaphore was not released, this second call would deadlock. Using a short-lived token to guard.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("after-throw"), cts.Token)
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("after-throw");
    }

    [Test]
    public async Task HandleAsync_ExclusiveVoidCommand_ExecutesHandler(CancellationToken cancellationToken)
    {
        using var interceptor = new ConcurrentCommandGuardInterceptor<ExclusiveVoidCommand, Extensibility.Void>();
        var command = new ExclusiveVoidCommand();
        var handlerCalled = false;

        var result = await interceptor
            .HandleAsync(
                command,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.FromResult(Extensibility.Void.Completed);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo(Extensibility.Void.Completed);
            _ = await Assert.That(handlerCalled).IsTrue();
        }
    }

    [Test]
    public async Task HandleAsync_ExclusiveCommand_SerializesExecution(CancellationToken cancellationToken)
    {
        using var interceptor = new ConcurrentCommandGuardInterceptor<ExclusiveCommand3, int>();
        var maxConcurrent = 0;
        var currentConcurrent = 0;
        var tasks = new Task<int>[5];

        for (var i = 0; i < tasks.Length; i++)
        {
            var command = new ExclusiveCommand3();
            tasks[i] = interceptor.HandleAsync(
                command,
                async (cmd, ct) =>
                {
                    var current = Interlocked.Increment(ref currentConcurrent);
                    var max = maxConcurrent;
                    while (current > max)
                    {
                        _ = Interlocked.CompareExchange(ref maxConcurrent, current, max);
                        max = maxConcurrent;
                    }

                    await Task.Delay(10, ct).ConfigureAwait(false);
                    _ = Interlocked.Decrement(ref currentConcurrent);
                    return current;
                },
                cancellationToken
            );
        }

        _ = await Task.WhenAll(tasks).ConfigureAwait(false);

        _ = await Assert.That(maxConcurrent).IsEqualTo(1);
    }

    private sealed record ExclusiveCommand : IExclusiveCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record ExclusiveCommand2 : IExclusiveCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record ExclusiveCommand3 : IExclusiveCommand<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record ExclusiveVoidCommand : IExclusiveCommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
