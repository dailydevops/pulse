namespace NetEvolve.Pulse.Tests.Unit.Polly.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using global::Polly;
using global::Polly.CircuitBreaker;
using global::Polly.Retry;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
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
[TestGroup("Polly")]
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
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    ) =>
        // Act & Assert
        _ = await Assert.That(() => new PollyEventInterceptor<TestEvent>(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NoPipelineRegistered_ThrowsInvalidOperationException(
        CancellationToken cancellationToken
    )
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
    public async Task Constructor_WithKeyedPipeline_ResolvesSuccessfully(CancellationToken cancellationToken)
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestEvent>(useKeyedService: true);

        // Act
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithGlobalPipeline_ResolvesSuccessfully(CancellationToken cancellationToken)
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestEvent>(useKeyedService: false);

        // Act
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestEvent>();
        var interceptor = new PollyEventInterceptor<TestEvent>(serviceProvider);
        var message = new TestEvent();

        // Act & Assert
        _ = await Assert
            .That(async () => await interceptor.HandleAsync(message, null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_WithSuccessfulHandler_CompletesSuccessfully(CancellationToken cancellationToken)
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
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(handlerCalled).IsTrue();
    }

    [Test]
    public async Task HandleAsync_WithRetryPolicy_RetriesOnFailure(CancellationToken cancellationToken)
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
                (_, _) =>
                {
                    attemptCount++;
                    if (attemptCount < 3)
                    {
                        throw new InvalidOperationException("Transient failure");
                    }
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(attemptCount).IsEqualTo(3);
    }

    [Test]
    public async Task HandleAsync_WithRetryPolicyExhausted_ThrowsException(CancellationToken cancellationToken)
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
                        (_, _) =>
                        {
                            attemptCount++;
                            throw new InvalidOperationException("Persistent failure");
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>()
            .WithMessage("Persistent failure", StringComparison.OrdinalIgnoreCase);

        // Verify retry attempts (initial + 2 retries = 3 total)
        _ = await Assert.That(attemptCount).IsEqualTo(3);
    }

    [Test]
    public async Task HandleAsync_WithCombinedPolicies_ExecutesInOrder(CancellationToken cancellationToken)
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
                (_, _) =>
                {
                    attemptCount++;
                    if (attemptCount < 2)
                    {
                        throw new InvalidOperationException("Transient failure");
                    }
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(attemptCount).IsEqualTo(2);
    }

    [Test]
    public async Task HandleAsync_WithCircuitBreaker_BlocksAfterFailureThreshold(CancellationToken cancellationToken)
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
                        (_, _) =>
                        {
                            attemptCount++;
                            throw new InvalidOperationException("Failure");
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(
                        message,
                        (_, _) =>
                        {
                            attemptCount++;
                            throw new InvalidOperationException("Failure");
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        // Circuit should be open now, next request should be rejected immediately
        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(message, (_, _) => Task.CompletedTask, cancellationToken)
                    .ConfigureAwait(false)
            )
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
