namespace NetEvolve.Pulse.Tests.Unit.HttpCorrelation.Interceptors;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Http.Correlation.Abstractions;
using NetEvolve.Http.Correlation.AspNetCore;
using NetEvolve.Http.Correlation.TestGenerator;
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
[TestGroup("HttpCorrelation")]
public sealed class HttpCorrelationStreamQueryInterceptorTests
{
    [Test]
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new HttpCorrelationStreamQueryInterceptor<TestStreamQuery, string>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NoAccessorRegistered_DoesNotThrow()
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();

        // Act
        var interceptor = new HttpCorrelationStreamQueryInterceptor<TestStreamQuery, string>(provider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithAccessorRegistered_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator("test-id");
        var provider = services.BuildServiceProvider();

        // Act
        var interceptor = new HttpCorrelationStreamQueryInterceptor<TestStreamQuery, string>(provider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new HttpCorrelationStreamQueryInterceptor<TestStreamQuery, string>(provider);
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
    public async Task HandleAsync_NoAccessorRegistered_PassesThroughWithoutModification(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new HttpCorrelationStreamQueryInterceptor<TestStreamQuery, string>(provider);
        var request = new TestStreamQuery { CorrelationId = null };

        // Act
        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(request, (_, ct) => YieldItemsAsync(["a", "b"], ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(items).IsEquivalentTo(["a", "b"]);
            _ = await Assert.That(request.CorrelationId).IsNull();
        }
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_RequestAlreadyHasCorrelationId_DoesNotOverwrite(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        const string existingId = "existing-id";
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator("http-id");
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationStreamQueryInterceptor<TestStreamQuery, string>(provider);
        var request = new TestStreamQuery { CorrelationId = existingId };

        // Act
        await foreach (
            var _ in interceptor
                .HandleAsync(request, (_, ct) => YieldItemsAsync(["x"], ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // consume
        }

        // Assert
        _ = await Assert.That(request.CorrelationId).IsEqualTo(existingId);
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_RequestHasNoCorrelationId_SetsCorrelationId(
        CancellationToken cancellationToken
    )
    {
        // Arrange
        const string httpId = "http-correlation-id";
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(httpId);
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // HttpCorrelationAccessor caches the correlation ID in a private field; set it via reflection
        // to simulate an incoming HTTP request that has already populated the correlation ID.
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpCorrelationAccessor>();
        var field = accessor.GetType().GetField("_correlationId", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(accessor, httpId);

        var interceptor = new HttpCorrelationStreamQueryInterceptor<TestStreamQuery, string>(scope.ServiceProvider);
        var request = new TestStreamQuery { CorrelationId = null };

        // Act
        await foreach (
            var _ in interceptor
                .HandleAsync(request, (_, ct) => YieldItemsAsync(["x"], ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // consume
        }

        // Assert
        _ = await Assert.That(request.CorrelationId).IsEqualTo(httpId);
    }

    [Test]
    public async Task HandleAsync_AccessorCorrelationIdIsEmpty_DoesNotModifyRequest(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(string.Empty);
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationStreamQueryInterceptor<TestStreamQuery, string>(provider);
        var request = new TestStreamQuery { CorrelationId = null };

        // Act
        await foreach (
            var _ in interceptor
                .HandleAsync(request, (_, ct) => YieldItemsAsync(["x"], ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // consume
        }

        // Assert
        _ = await Assert.That(request.CorrelationId).IsNull();
    }

    [Test]
    public async Task HandleAsync_YieldsItemsUnchanged(CancellationToken cancellationToken)
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new HttpCorrelationStreamQueryInterceptor<TestStreamQuery, string>(provider);
        var request = new TestStreamQuery();
        var expected = new[] { "item1", "item2", "item3" };

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

    private sealed record TestStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }
    }
}
