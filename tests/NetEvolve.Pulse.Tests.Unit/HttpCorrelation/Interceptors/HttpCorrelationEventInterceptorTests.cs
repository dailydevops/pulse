namespace NetEvolve.Pulse.Tests.Unit.HttpCorrelation.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class HttpCorrelationEventInterceptorTests
{
    [Test]
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new HttpCorrelationEventInterceptor<TestEvent>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NoAccessorRegistered_DoesNotThrow()
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();

        // Act
        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithAccessorRegistered_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator("test-id");
        var provider = services.BuildServiceProvider();

        // Act
        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);
        var message = new TestEvent();

        // Act & Assert
        _ = await Assert
            .That(async () => await interceptor.HandleAsync(message, null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoAccessorRegistered_PassesThroughWithoutModification()
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);
        var message = new TestEvent { CorrelationId = null };
        var handlerCalled = false;

        // Act
        await interceptor
            .HandleAsync(
                message,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.CompletedTask;
                }
            )
            .ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(message.CorrelationId).IsNull();
        }
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_MessageCorrelationIdIsNull_SetsCorrelationId()
    {
        // Arrange
        const string expectedId = "correlation-abc";
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator(expectedId);
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);
        var message = new TestEvent { CorrelationId = null };

        // Act
        await interceptor.HandleAsync(message, (_, _) => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(message.CorrelationId).IsEqualTo(expectedId);
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_MessageCorrelationIdIsEmpty_SetsCorrelationId()
    {
        // Arrange
        const string expectedId = "correlation-abc";
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator(expectedId);
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);
        var message = new TestEvent { CorrelationId = string.Empty };

        // Act
        await interceptor.HandleAsync(message, (_, _) => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(message.CorrelationId).IsEqualTo(expectedId);
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_MessageAlreadyHasCorrelationId_DoesNotOverwrite()
    {
        // Arrange
        const string existingId = "existing-id";
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator("http-id");
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);
        var message = new TestEvent { CorrelationId = existingId };

        // Act
        await interceptor.HandleAsync(message, (_, _) => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(message.CorrelationId).IsEqualTo(existingId);
    }

    [Test]
    public async Task HandleAsync_AccessorCorrelationIdIsEmpty_DoesNotModifyMessage()
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services.AddHttpCorrelation().WithTestGenerator(string.Empty);
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);
        var message = new TestEvent { CorrelationId = null };

        // Act
        await interceptor.HandleAsync(message, (_, _) => Task.CompletedTask).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(message.CorrelationId).IsNull();
    }

    private sealed record TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
