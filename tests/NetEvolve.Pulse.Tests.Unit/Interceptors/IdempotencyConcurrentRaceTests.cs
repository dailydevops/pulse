namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

/// <summary>
/// Phase 2 audit verification — Q04.
/// <see cref="IdempotencyCommandInterceptor{TRequest, TResponse}"/> implements the classic
/// check-then-act pattern (<c>ExistsAsync</c> → handler → <c>StoreAsync</c>). Two concurrent
/// commands with the same idempotency key both pass <c>ExistsAsync</c> before either calls
/// <c>StoreAsync</c>, so the handler runs twice.
///
/// EXPECTED TO FAIL today: the test schedules two parallel <c>HandleAsync</c> invocations
/// with the same key, gates the handler so neither can finish before both have entered,
/// then asserts that the handler ran at most once and the second invocation surfaced
/// <see cref="IdempotencyConflictException"/>.
/// </summary>
[SuppressMessage(
    "IDisposableAnalyzers.Correctness",
    "CA2000:Dispose objects before losing scope",
    Justification = "ServiceProvider instances are short-lived within test methods"
)]
[TestGroup("Audit-Q04")]
public sealed class IdempotencyConcurrentRaceTests
{
    [Test]
    public async Task HandleAsync_Two_Concurrent_Commands_Same_Key_Should_Execute_Handler_Once()
    {
        var store = new SharedTrackingIdempotencyStore();
        var services = new ServiceCollection();
        _ = services.AddSingleton<IIdempotencyStore>(store);
        var provider = services.BuildServiceProvider();

        // Two interceptors that share the same store (same scenario as two competing
        // workers under one DI container resolving the singleton store).
        var interceptor1 = new IdempotencyCommandInterceptor<RaceCommand, string>(provider);
        var interceptor2 = new IdempotencyCommandInterceptor<RaceCommand, string>(provider);

        var sharedKey = "race-key";
        var entered = new SemaphoreSlim(0, 2);
        var release = new TaskCompletionSource();
        var handlerExecutionCount = 0;

        async Task<string> GatedHandler(RaceCommand cmd, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(cmd);
            _ = Interlocked.Increment(ref handlerExecutionCount);
            _ = entered.Release();
            await release.Task.WaitAsync(ct).ConfigureAwait(false);
            return "ok";
        }

        var command1 = new RaceCommand { IdempotencyKey = sharedKey };
        var command2 = new RaceCommand { IdempotencyKey = sharedKey };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Schedule both invocations; do not await yet.
        var task1 = Task.Run(() => interceptor1.HandleAsync(command1, GatedHandler, cts.Token), cts.Token);
        var task2 = Task.Run(() => interceptor2.HandleAsync(command2, GatedHandler, cts.Token), cts.Token);

        // Wait for either both handlers to enter (the race we want) OR a single conflict to be raised.
        // If the interceptor were race-safe, only ONE handler would enter and the other would surface
        // an IdempotencyConflictException without ever calling the handler.
        var firstEntered = await entered.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var secondEntered = await entered.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var bothEntered = firstEntered && secondEntered;

        // Release whichever handlers are parked.
        release.SetResult();

        // Await both invocations; collect their outcomes.
        var outcomes = new[]
        {
            await CaptureAsync(task1).ConfigureAwait(false),
            await CaptureAsync(task2).ConfigureAwait(false),
        };

        // ASSERTION CAPTURES THE DEFECT:
        // The handler must execute at most ONCE for the same idempotency key, even under concurrency.
        // Today both handlers execute (handlerExecutionCount == 2) because the check-then-act window is open.
        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerExecutionCount).IsEqualTo(1);
            _ = await Assert
                .That(System.Array.Exists(outcomes, o => o.Exception is IdempotencyConflictException))
                .IsTrue();
            // Sanity-record: if this is true the race was provoked; if false the test is non-reproducing.
            // We do not assert on it — leave it as diagnostic.
            _ = bothEntered;
        }
    }

    [SuppressMessage(
        "Reliability",
        "VSTHRD003:Avoid awaiting foreign Tasks",
        Justification = "Test deliberately awaits Task.Run-produced tasks to capture outcomes."
    )]
    private static async Task<(string? Result, Exception? Exception)> CaptureAsync(Task<string> task)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private sealed record RaceCommand : IIdempotentCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string IdempotencyKey { get; init; } = string.Empty;
    }

    /// <summary>
    /// A store that mirrors the real <see cref="IdempotencyStore"/> contract: ExistsAsync and
    /// StoreAsync are NON-ATOMIC. Internally tracks stored keys with a lock for thread safety,
    /// but does NOT implement check-and-set semantics.
    /// </summary>
    private sealed class SharedTrackingIdempotencyStore : IIdempotencyStore
    {
        private readonly System.Collections.Generic.HashSet<string> _keys = [];
        private readonly object _lock = new();

        public Task<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                return Task.FromResult(_keys.Contains(idempotencyKey));
            }
        }

        public Task StoreAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _ = _keys.Add(idempotencyKey);
            }
            return Task.CompletedTask;
        }
    }
}
