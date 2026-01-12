namespace NetEvolve.Pulse.Polly.Tests.Unit.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using global::Polly;
using global::Polly.CircuitBreaker;
using global::Polly.Retry;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[SuppressMessage(
    "IDisposableAnalyzers.Correctness",
    "CA2000:Dispose objects before losing scope",
    Justification = "ServiceProvider instances are short-lived within test methods"
)]
public sealed class PollyEventInterceptorTests
{
    private static ServiceProvider CreateServiceProvider<TEvent>(
        ResiliencePipeline? pipeline = null,
        bool useKeyedService = true
    )
        where TEvent : IEvent
    {
        var services = new ServiceCollection();

        pipeline ??= new ResiliencePipelineBuilder().Build();

        if (useKeyedService)
        {
            _ = services.AddKeyedSingleton<ResiliencePipeline>(typeof(TEvent), pipeline);
        }
        else
        {
            _ = services.AddSingleton(pipeline);
        }

        return services.BuildServiceProvider();
    }

    [Test]
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException() =>
        // Act & Assert
        _ = await Assert.That(() => new PollyEventInterceptor<TestEvent>(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NoPipelineRegistered_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        _ = await Assert
            .That(() => new PollyEventInterceptor<TestEvent>(serviceProvider))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Constructor_WithKeyedPipeline_ResolvesSuccessfully()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestEvent>(useKeyedService: true);

        // Act
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithGlobalPipeline_ResolvesSuccessfully()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestEvent>(useKeyedService: false);

        // Act
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestEvent>();
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);
        var message = new TestEvent();

        // Act & Assert
        _ = await Assert
            .That(async () => await interceptor.HandleAsync(message, null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_WithSuccessfulHandler_CompletesSuccessfully()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestEvent>();
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);
        var message = new TestEvent();
        var handlerCalled = false;

        // Act
        await interceptor
            .HandleAsync(
                message,
                async evt =>
                {
                    handlerCalled = true;
                    await Task.CompletedTask.ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(handlerCalled).IsTrue();
    }

    [Test]
    public async Task HandleAsync_WithRetryPolicy_RetriesOnFailure()
    {
        // Arrange
        var attemptCount = 0;
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(10),
                    BackoffType = DelayBackoffType.Constant,
                }
            )
            .Build();

        var serviceProvider = CreateServiceProvider<TestEvent>(pipeline);
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);
        var message = new TestEvent();

        // Act
        await interceptor
            .HandleAsync(
                message,
                evt =>
                {
                    attemptCount++;
                    if (attemptCount < 3)
                    {
                        throw new InvalidOperationException("Transient failure");
                    }
                    return Task.CompletedTask;
                }
            )
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(attemptCount).IsEqualTo(3);
    }

    [Test]
    public async Task HandleAsync_WithRetryPolicyExhausted_ThrowsException()
    {
        // Arrange
        var attemptCount = 0;
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(10),
                    BackoffType = DelayBackoffType.Constant,
                }
            )
            .Build();

        var serviceProvider = CreateServiceProvider<TestEvent>(pipeline);
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);
        var message = new TestEvent();

        // Act & Assert
        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(
                        message,
                        evt =>
                        {
                            attemptCount++;
                            throw new InvalidOperationException("Persistent failure");
                        }
                    )
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>()
            .WithMessage("Persistent failure", StringComparison.OrdinalIgnoreCase);

        // Verify retry attempts (initial + 2 retries = 3 total)
        _ = await Assert.That(attemptCount).IsEqualTo(3);
    }

    [Test]
    public async Task HandleAsync_WithCombinedPolicies_ExecutesInOrder()
    {
        // Arrange
        var attemptCount = 0;
        var pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(5)) // Outer policy
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(10),
                    BackoffType = DelayBackoffType.Constant,
                }
            ) // Inner policy
            .Build();

        var serviceProvider = CreateServiceProvider<TestEvent>(pipeline);
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);
        var message = new TestEvent();

        // Act
        await interceptor
            .HandleAsync(
                message,
                evt =>
                {
                    attemptCount++;
                    if (attemptCount < 2)
                    {
                        throw new InvalidOperationException("Transient failure");
                    }
                    return Task.CompletedTask;
                }
            )
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(attemptCount).IsEqualTo(2);
    }

    [Test]
    public async Task HandleAsync_WithCircuitBreaker_BlocksAfterFailureThreshold()
    {
        // Arrange
        var attemptCount = 0;
        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(
                new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 2,
                    BreakDuration = TimeSpan.FromMilliseconds(500),
                    SamplingDuration = TimeSpan.FromSeconds(10),
                }
            )
            .Build();

        var serviceProvider = CreateServiceProvider<TestEvent>(pipeline);
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);
        var message = new TestEvent();

        // Act & Assert - First two requests fail
        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(
                        message,
                        evt =>
                        {
                            attemptCount++;
                            throw new InvalidOperationException("Failure");
                        }
                    )
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(
                        message,
                        evt =>
                        {
                            attemptCount++;
                            throw new InvalidOperationException("Failure");
                        }
                    )
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        // Circuit should be open now, next request should be rejected immediately
        _ = await Assert
            .That(async () => await interceptor.HandleAsync(message, evt => Task.CompletedTask).ConfigureAwait(false))
            .Throws<BrokenCircuitException>();

        // Verify handler was only called twice (not on third attempt due to open circuit)
        _ = await Assert.That(attemptCount).IsEqualTo(2);
    }

    private sealed record TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
