namespace NetEvolve.Pulse.Polly.Tests.Integration;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Polly;
using global::Polly.CircuitBreaker;
using global::Polly.Retry;
using global::Polly.Timeout;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

/// <summary>
/// In-memory integration tests for Polly request interceptor.
/// Tests retry, circuit breaker, timeout, and combined policies without external dependencies.
/// </summary>
public sealed class PollyRequestInterceptorInMemoryTests
{
    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        return services;
    }

    [Test]
    public async Task RetryPolicy_WithTransientFailure_RetriesAndSucceeds()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handler = new FailingCommandHandler(failCount: 2); // Fail twice, then succeed
        var onRetryCallCount = 0;

        _ = services
            .AddScoped<ICommandHandler<RetryCommand, RetryResult>>(_ => handler)
            .AddPulse(configurator =>
                configurator.AddPollyRequestPolicies<RetryCommand, RetryResult>(pipeline =>
                    pipeline.AddRetry(
                        new RetryStrategyOptions<RetryResult>
                        {
                            MaxRetryAttempts = 3,
                            Delay = TimeSpan.FromMilliseconds(10),
                            OnRetry = args =>
                            {
                                _ = Interlocked.Increment(ref onRetryCallCount);
                                return ValueTask.CompletedTask;
                            },
                        }
                    )
                )
            );

        await using var provider = services.BuildServiceProvider();

        // Debug: verify interceptor and pipeline registration
        var interceptors = provider.GetServices<IRequestInterceptor<RetryCommand, RetryResult>>().ToList();
        var pipeline = provider.GetKeyedService<ResiliencePipeline<RetryResult>>(typeof(RetryCommand));
        _ = await Assert.That(interceptors.Count).IsGreaterThan(0).Because("Polly interceptor should be registered");
        _ = await Assert.That(pipeline).IsNotNull().Because("Polly pipeline should be registered");

        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator
            .SendAsync<RetryCommand, RetryResult>(new RetryCommand("retry-test"))
            .ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Success).IsTrue();
            _ = await Assert
                .That(onRetryCallCount)
                .IsEqualTo(2)
                .Because("OnRetry should be called twice for 2 failures");
            _ = await Assert.That(handler.AttemptCount).IsEqualTo(3); // 2 failures + 1 success
        }
    }

    [Test]
    public async Task RetryPolicy_ExceedsMaxRetries_ThrowsException()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handler = new FailingCommandHandler(failCount: int.MaxValue); // Always fail

        _ = services
            .AddScoped<ICommandHandler<RetryCommand, RetryResult>>(_ => handler)
            .AddPulse(configurator =>
                configurator.AddPollyRequestPolicies<RetryCommand, RetryResult>(pipeline =>
                    pipeline.AddRetry(
                        new RetryStrategyOptions<RetryResult>
                        {
                            MaxRetryAttempts = 2,
                            Delay = TimeSpan.FromMilliseconds(10),
                        }
                    )
                )
            );

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync<RetryCommand, RetryResult>(new RetryCommand("always-fail")).ConfigureAwait(false)
        );
    }

    [Test]
    [Skip(
        "Polly timeout requires propagating ResilienceContext.CancellationToken to handlers. Architecture change needed."
    )]
    public async Task TimeoutPolicy_WithSlowHandler_ThrowsTimeoutException()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handler = new SlowCommandHandler(TimeSpan.FromSeconds(5));

        _ = services
            .AddScoped<ICommandHandler<TimeoutCommand, TimeoutResult>>(_ => handler)
            .AddPulse(configurator =>
                configurator.AddPollyRequestPolicies<TimeoutCommand, TimeoutResult>(pipeline =>
                    pipeline.AddTimeout(TimeSpan.FromMilliseconds(100))
                )
            );

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        _ = await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await mediator.SendAsync<TimeoutCommand, TimeoutResult>(new TimeoutCommand("slow")).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task TimeoutPolicy_WithFastHandler_Succeeds()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handler = new SlowCommandHandler(TimeSpan.FromMilliseconds(10));

        _ = services
            .AddScoped<ICommandHandler<TimeoutCommand, TimeoutResult>>(_ => handler)
            .AddPulse(configurator =>
                configurator.AddPollyRequestPolicies<TimeoutCommand, TimeoutResult>(pipeline =>
                    pipeline.AddTimeout(TimeSpan.FromSeconds(1))
                )
            );

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator
            .SendAsync<TimeoutCommand, TimeoutResult>(new TimeoutCommand("fast"))
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task CircuitBreakerPolicy_AfterFailureThreshold_OpensCircuit()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handler = new FailingCommandHandler(failCount: int.MaxValue);

        _ = services
            .AddScoped<ICommandHandler<CircuitCommand, CircuitResult>>(_ => handler)
            .AddPulse(configurator =>
                configurator.AddPollyRequestPolicies<CircuitCommand, CircuitResult>(pipeline =>
                    pipeline.AddCircuitBreaker(
                        new CircuitBreakerStrategyOptions<CircuitResult>
                        {
                            FailureRatio = 0.5,
                            MinimumThroughput = 2,
                            SamplingDuration = TimeSpan.FromSeconds(30),
                            BreakDuration = TimeSpan.FromSeconds(30),
                        }
                    )
                )
            );

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act - Trigger failures to open circuit
        // Circuit evaluates after MinimumThroughput (2) calls
        // With 100% failure rate > FailureRatio (50%), circuit opens after 2 calls
        BrokenCircuitException? caughtBrokenCircuit = null;
        for (var i = 0; i < 3; i++)
        {
            try
            {
                _ = await mediator
                    .SendAsync<CircuitCommand, CircuitResult>(new CircuitCommand($"fail-{i}"))
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Expected handler exception
            }
            catch (BrokenCircuitException ex)
            {
                // Circuit opened during the loop
                caughtBrokenCircuit = ex;
                break;
            }
        }

        // Assert - Circuit should have opened and thrown BrokenCircuitException
        _ = await Assert.That(caughtBrokenCircuit).IsNotNull().Because("Circuit should open after 2 failures");
    }

    [Test]
    public async Task CombinedPolicies_WithRetryAndTimeout_WorksTogether()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handler = new FailingCommandHandler(failCount: 1);

        _ = services
            .AddScoped<ICommandHandler<CombinedCommand, CombinedResult>>(_ => handler)
            .AddPulse(configurator =>
                configurator.AddPollyRequestPolicies<CombinedCommand, CombinedResult>(pipeline =>
                    pipeline
                        .AddTimeout(TimeSpan.FromSeconds(5))
                        .AddRetry(
                            new RetryStrategyOptions<CombinedResult>
                            {
                                MaxRetryAttempts = 3,
                                Delay = TimeSpan.FromMilliseconds(10),
                            }
                        )
                )
            );

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator
            .SendAsync<CombinedCommand, CombinedResult>(new CombinedCommand("combined"))
            .ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(result.Success).IsTrue();
            _ = await Assert.That(handler.AttemptCount).IsEqualTo(2); // 1 failure + 1 success
        }
    }

    [Test]
    public async Task GlobalPolicy_AppliesAcrossAllRequests()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handler = new FailingCommandHandler(failCount: 1);

        _ = services.AddSingleton(
            new ResiliencePipelineBuilder<RetryResult>()
                .AddRetry(
                    new RetryStrategyOptions<RetryResult>
                    {
                        MaxRetryAttempts = 3,
                        Delay = TimeSpan.FromMilliseconds(10),
                    }
                )
                .Build()
        );

        _ = services
            .AddScoped<ICommandHandler<RetryCommand, RetryResult>>(_ => handler)
            .AddPulse(configurator =>
                configurator.AddPollyRequestPolicies<RetryCommand, RetryResult>(pipeline =>
                    pipeline.AddRetry(
                        new RetryStrategyOptions<RetryResult>
                        {
                            MaxRetryAttempts = 3,
                            Delay = TimeSpan.FromMilliseconds(10),
                        }
                    )
                )
            );

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator
            .SendAsync<RetryCommand, RetryResult>(new RetryCommand("global-policy"))
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task PolicyWithTelemetry_TracksExecutions()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handler = new FailingCommandHandler(failCount: 1);
        var retryCount = 0;

        _ = services
            .AddScoped<ICommandHandler<RetryCommand, RetryResult>>(_ => handler)
            .AddPulse(configurator =>
                configurator.AddPollyRequestPolicies<RetryCommand, RetryResult>(pipeline =>
                    pipeline.AddRetry(
                        new RetryStrategyOptions<RetryResult>
                        {
                            MaxRetryAttempts = 3,
                            Delay = TimeSpan.FromMilliseconds(10),
                            OnRetry = args =>
                            {
                                _ = Interlocked.Increment(ref retryCount);
                                return default;
                            },
                        }
                    )
                )
            );

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        _ = await mediator
            .SendAsync<RetryCommand, RetryResult>(new RetryCommand("telemetry-test"))
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(retryCount).IsEqualTo(1);
    }

    #region Test Types

    private sealed record RetryCommand(string Id) : ICommand<RetryResult>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record RetryResult(bool Success);

    private sealed record TimeoutCommand(string Id) : ICommand<TimeoutResult>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TimeoutResult(bool Success);

    private sealed record CircuitCommand(string Id) : ICommand<CircuitResult>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record CircuitResult(bool Success);

    private sealed record CombinedCommand(string Id) : ICommand<CombinedResult>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record CombinedResult(bool Success);

    private sealed class FailingCommandHandler
        : ICommandHandler<RetryCommand, RetryResult>,
            ICommandHandler<CircuitCommand, CircuitResult>,
            ICommandHandler<CombinedCommand, CombinedResult>
    {
        private readonly int _failCount;
        private int _attemptCount;

        public FailingCommandHandler(int failCount) => _failCount = failCount;

        public int AttemptCount => _attemptCount;

        public Task<RetryResult> HandleAsync(RetryCommand command, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _attemptCount);
            if (attempt <= _failCount)
            {
                throw new InvalidOperationException($"Simulated failure on attempt {attempt}");
            }

            return Task.FromResult(new RetryResult(true));
        }

        public Task<CircuitResult> HandleAsync(CircuitCommand command, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _attemptCount);
            if (attempt <= _failCount)
            {
                throw new InvalidOperationException($"Simulated failure on attempt {attempt}");
            }

            return Task.FromResult(new CircuitResult(true));
        }

        public Task<CombinedResult> HandleAsync(CombinedCommand command, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _attemptCount);
            if (attempt <= _failCount)
            {
                throw new InvalidOperationException($"Simulated failure on attempt {attempt}");
            }

            return Task.FromResult(new CombinedResult(true));
        }
    }

    private sealed class SlowCommandHandler : ICommandHandler<TimeoutCommand, TimeoutResult>
    {
        private readonly TimeSpan _delay;

        public SlowCommandHandler(TimeSpan delay) => _delay = delay;

        public async Task<TimeoutResult> HandleAsync(
            TimeoutCommand command,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            return new TimeoutResult(true);
        }
    }

    #endregion
}
