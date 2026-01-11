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
public sealed class PollyRequestInterceptorTests
{
    private static ServiceProvider CreateServiceProvider<TRequest, TResponse>(
        ResiliencePipeline<TResponse>? pipeline = null,
        bool useKeyedService = true
    )
        where TRequest : IRequest<TResponse>
    {
        var services = new ServiceCollection();

        pipeline ??= new ResiliencePipelineBuilder<TResponse>().Build();

        if (useKeyedService)
        {
            _ = services.AddKeyedSingleton<ResiliencePipeline<TResponse>>(typeof(TRequest), pipeline);
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
        _ = await Assert
            .That(() => new PollyRequestInterceptor<TestCommand, string>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NoPipelineRegistered_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        _ = await Assert
            .That(() => new PollyRequestInterceptor<TestCommand, string>(serviceProvider))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Constructor_WithKeyedPipeline_ResolvesSuccessfully()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestCommand, string>(useKeyedService: true);

        // Act
        var interceptor = new PollyRequestInterceptor<TestCommand, string>(serviceProvider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithGlobalPipeline_ResolvesSuccessfully()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestCommand, string>(useKeyedService: false);

        // Act
        var interceptor = new PollyRequestInterceptor<TestCommand, string>(serviceProvider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestCommand, string>();
        var interceptor = new PollyRequestInterceptor<TestCommand, string>(serviceProvider);
        var request = new TestCommand();

        // Act & Assert
        _ = await Assert
            .That(async () => await interceptor.HandleAsync(request, null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_WithSuccessfulHandler_ReturnsResult()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestCommand, string>();
        var interceptor = new PollyRequestInterceptor<TestCommand, string>(serviceProvider);
        var request = new TestCommand();
        const string expected = "success";

        // Act
        var result = await interceptor.HandleAsync(request, _ => Task.FromResult(expected));

        // Assert
        _ = await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task HandleAsync_WithRetryPolicy_RetriesOnFailure()
    {
        // Arrange
        var attemptCount = 0;
        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRetry(
                new RetryStrategyOptions<string>
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(10),
                    BackoffType = DelayBackoffType.Constant,
                }
            )
            .Build();

        var serviceProvider = CreateServiceProvider<TestCommand, string>(pipeline);
        var interceptor = new PollyRequestInterceptor<TestCommand, string>(serviceProvider);
        var request = new TestCommand();

        // Act
        var result = await interceptor.HandleAsync(
            request,
            _ =>
            {
                attemptCount++;
                if (attemptCount < 3)
                {
                    throw new InvalidOperationException("Transient failure");
                }
                return Task.FromResult("success");
            }
        );

        // Assert
        _ = await Assert.That(result).IsEqualTo("success");
        _ = await Assert.That(attemptCount).IsEqualTo(3);
    }

    [Test]
    public async Task HandleAsync_WithRetryPolicyExhausted_ThrowsException()
    {
        // Arrange
        var attemptCount = 0;
        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRetry(
                new RetryStrategyOptions<string>
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(10),
                    BackoffType = DelayBackoffType.Constant,
                }
            )
            .Build();

        var serviceProvider = CreateServiceProvider<TestCommand, string>(pipeline);
        var interceptor = new PollyRequestInterceptor<TestCommand, string>(serviceProvider);
        var request = new TestCommand();

        // Act & Assert
        _ = await Assert
            .That(async () =>
                await interceptor.HandleAsync(
                    request,
                    _ =>
                    {
                        attemptCount++;
                        throw new InvalidOperationException("Persistent failure");
                    }
                )
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
        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddTimeout(TimeSpan.FromSeconds(5)) // Outer policy
            .AddRetry(
                new RetryStrategyOptions<string>
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(10),
                    BackoffType = DelayBackoffType.Constant,
                }
            ) // Inner policy
            .Build();

        var serviceProvider = CreateServiceProvider<TestCommand, string>(pipeline);
        var interceptor = new PollyRequestInterceptor<TestCommand, string>(serviceProvider);
        var request = new TestCommand();

        // Act
        var result = await interceptor.HandleAsync(
            request,
            _ =>
            {
                attemptCount++;
                if (attemptCount < 2)
                {
                    throw new InvalidOperationException("Transient failure");
                }
                return Task.FromResult("success");
            }
        );

        // Assert
        _ = await Assert.That(result).IsEqualTo("success");
        _ = await Assert.That(attemptCount).IsEqualTo(2);
    }

    [Test]
    public async Task HandleAsync_WithCircuitBreaker_BlocksAfterFailureThreshold()
    {
        // Arrange
        var attemptCount = 0;
        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddCircuitBreaker(
                new CircuitBreakerStrategyOptions<string>
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 2,
                    BreakDuration = TimeSpan.FromMilliseconds(500),
                    SamplingDuration = TimeSpan.FromSeconds(10),
                }
            )
            .Build();

        var serviceProvider = CreateServiceProvider<TestCommand, string>(pipeline);
        var interceptor = new PollyRequestInterceptor<TestCommand, string>(serviceProvider);
        var request = new TestCommand();

        // Act & Assert - First two requests fail
        _ = await Assert
            .That(async () =>
                await interceptor.HandleAsync(
                    request,
                    _ =>
                    {
                        attemptCount++;
                        throw new InvalidOperationException("Failure");
                    }
                )
            )
            .Throws<InvalidOperationException>();

        _ = await Assert
            .That(async () =>
                await interceptor.HandleAsync(
                    request,
                    _ =>
                    {
                        attemptCount++;
                        throw new InvalidOperationException("Failure");
                    }
                )
            )
            .Throws<InvalidOperationException>();

        // Circuit should be open now, next request should be rejected immediately
        _ = await Assert
            .That(async () => await interceptor.HandleAsync(request, _ => Task.FromResult("success")))
            .Throws<BrokenCircuitException>();

        // Verify handler was only called twice (not on third attempt due to open circuit)
        _ = await Assert.That(attemptCount).IsEqualTo(2);
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }
}
