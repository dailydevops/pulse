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

    private sealed record ExclusiveCommand : IExclusiveCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
