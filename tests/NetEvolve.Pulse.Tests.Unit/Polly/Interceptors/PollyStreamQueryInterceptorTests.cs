namespace NetEvolve.Pulse.Tests.Unit.Polly.Interceptors;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
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
public sealed class PollyStreamQueryInterceptorTests
{
    private static ServiceProvider CreateServiceProvider<TQuery>(
        ResiliencePipeline? pipeline = null,
        bool useKeyedService = true
    )
        where TQuery : IStreamQuery<string>
    {
        var services = new ServiceCollection();

        if (pipeline is not null)
        {
            if (useKeyedService)
            {
                _ = services.AddKeyedSingleton<ResiliencePipeline>(typeof(TQuery), pipeline);
            }
            else
            {
                _ = services.AddSingleton(pipeline);
            }
        }

        return services.BuildServiceProvider();
    }

    private static async IAsyncEnumerable<string> YieldItemsAsync(
        IEnumerable<string> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return item;
        }
    }

    [Test]
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException() =>
        // Act & Assert
        _ = await Assert
            .That(() => new PollyStreamQueryInterceptor<TestStreamQuery, string>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NoPipelineRegistered_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);

        // Assert — pass-through scenario: no exception
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithKeyedPipeline_ResolvesSuccessfully()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestStreamQuery>(
            new ResiliencePipelineBuilder().Build(),
            useKeyedService: true
        );

        // Act
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithGlobalPipeline_ResolvesSuccessfully()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestStreamQuery>(
            new ResiliencePipelineBuilder().Build(),
            useKeyedService: false
        );

        // Act
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestStreamQuery>(new ResiliencePipelineBuilder().Build());
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);
        var request = new TestStreamQuery();

        // Act & Assert
        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor.HandleAsync(request, null!, cancellationToken).ConfigureAwait(false)
                )
                {
                    // consume — we expect the foreach to throw before yielding any items
                }
            })
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoPipelineRegistered_PassesThroughTransparently(CancellationToken cancellationToken)
    {
        // Arrange
        var serviceProvider = CreateServiceProvider<TestStreamQuery>(pipeline: null);
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);
        var request = new TestStreamQuery();
        var expected = new[] { "a", "b", "c" };

        // Act
        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(request, (_, ct) => YieldItemsAsync(expected, ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        // Assert
        _ = await Assert.That(items).IsEquivalentTo(expected);
    }

    [Test]
    public async Task HandleAsync_WithSuccessfulPipeline_YieldsAllItems(CancellationToken cancellationToken)
    {
        // Arrange
        var pipeline = new ResiliencePipelineBuilder().Build();
        var serviceProvider = CreateServiceProvider<TestStreamQuery>(pipeline);
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);
        var request = new TestStreamQuery();
        var expected = new[] { "x", "y", "z" };

        // Act
        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(request, (_, ct) => YieldItemsAsync(expected, ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        // Assert
        _ = await Assert.That(items).IsEquivalentTo(expected);
    }

    [Test]
    public async Task HandleAsync_WithRetryPolicy_RetriesHandlerOnFailure(CancellationToken cancellationToken)
    {
        // Arrange
        var callCount = 0;
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

        var serviceProvider = CreateServiceProvider<TestStreamQuery>(pipeline);
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);
        var request = new TestStreamQuery();

        // Act
        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(
                    request,
                    (_, ct) =>
                    {
                        callCount++;
                        if (callCount < 3)
                        {
                            throw new InvalidOperationException("Transient failure during stream open");
                        }

                        return YieldItemsAsync(["item1", "item2"], ct);
                    },
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        // Assert
        _ = await Assert.That(callCount).IsEqualTo(3);
        _ = await Assert.That(items).IsEquivalentTo(["item1", "item2"]);
    }

    [Test]
    public async Task HandleAsync_WithRetryPolicyExhausted_ThrowsException(CancellationToken cancellationToken)
    {
        // Arrange
        var callCount = 0;
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

        var serviceProvider = CreateServiceProvider<TestStreamQuery>(pipeline);
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);
        var request = new TestStreamQuery();

        // Act & Assert
        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(
                            request,
                            (_, _) =>
                            {
                                callCount++;
                                throw new InvalidOperationException("Persistent failure");
                            },
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                )
                {
                    // consume
                }
            })
            .Throws<InvalidOperationException>()
            .WithMessage("Persistent failure", StringComparison.OrdinalIgnoreCase);

        // Verify retry attempts (initial + 2 retries = 3 total)
        _ = await Assert.That(callCount).IsEqualTo(3);
    }

    [Test]
    public async Task HandleAsync_WithCircuitBreaker_BlocksAfterFailureThreshold(CancellationToken cancellationToken)
    {
        // Arrange
        var callCount = 0;
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

        var serviceProvider = CreateServiceProvider<TestStreamQuery>(pipeline);
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);
        var request = new TestStreamQuery();

        // First two invocations fail, opening the circuit
        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(
                            request,
                            (_, _) =>
                            {
                                callCount++;
                                throw new InvalidOperationException("Failure");
                            },
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                )
                {
                    // consume
                }
            })
            .Throws<InvalidOperationException>();

        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(
                            request,
                            (_, _) =>
                            {
                                callCount++;
                                throw new InvalidOperationException("Failure");
                            },
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                )
                {
                    // consume
                }
            })
            .Throws<InvalidOperationException>();

        // Circuit should be open now, next request is rejected immediately
        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(request, (_, ct) => YieldItemsAsync(["item"], ct), cancellationToken)
                        .ConfigureAwait(false)
                )
                {
                    // consume
                }
            })
            .Throws<BrokenCircuitException>();

        // Handler was only called twice (not on third attempt due to open circuit)
        _ = await Assert.That(callCount).IsEqualTo(2);
    }

    [Test]
    public async Task HandleAsync_WithCancellation_ThrowsOperationCanceledException(CancellationToken cancellationToken)
    {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pipeline = new ResiliencePipelineBuilder().Build();
        var serviceProvider = CreateServiceProvider<TestStreamQuery>(pipeline);
        var interceptor = new PollyStreamQueryInterceptor<TestStreamQuery, string>(serviceProvider);
        var request = new TestStreamQuery();

        await cts.CancelAsync().ConfigureAwait(false);

        // Act & Assert
        _ = await Assert
            .That(async () =>
            {
                await foreach (
                    var _ in interceptor
                        .HandleAsync(request, (_, ct) => YieldItemsAsync(["item1", "item2"], ct), cts.Token)
                        .ConfigureAwait(false)
                )
                {
                    // consume
                }
            })
            .Throws<OperationCanceledException>();
    }

    private sealed record TestStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }
    }
}
