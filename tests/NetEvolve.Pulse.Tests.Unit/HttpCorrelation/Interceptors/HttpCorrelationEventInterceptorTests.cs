namespace NetEvolve.Pulse.Tests.Unit.HttpCorrelation.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator("test-id");
        var provider = services.BuildServiceProvider();

        // Act
        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);

        // Assert
        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        // Arrange
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);
        var message = new TestEvent();

        // Act & Assert
        _ = await Assert
            .That(async () => await interceptor.HandleAsync(message, null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NoAccessorRegistered_PassesThroughWithoutModification(
        CancellationToken cancellationToken
    )
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
                },
                cancellationToken
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
    public async Task HandleAsync_AccessorHasCorrelationId_MessageAlreadyHasCorrelationId_DoesNotOverwrite(
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

        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);
        var message = new TestEvent { CorrelationId = existingId };

        // Act
        await interceptor.HandleAsync(message, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(message.CorrelationId).IsEqualTo(existingId);
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_MessageHasNoCorrelationId_SetsCorrelationId(
        CancellationToken cancellationToken
    )
    {
        // Arrange — verifies the core invariant: when accessor exposes a correlation id and the
        // event message has none, the interceptor propagates the accessor value onto the message
        // before the next handler in the chain is invoked.
        const string httpId = "http-correlation-id";
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(httpId);
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpCorrelationAccessor>();
        var field = accessor.GetType().GetField("_correlationId", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(accessor, httpId);

        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(scope.ServiceProvider);
        var message = new TestEvent { CorrelationId = null };
        string? observedCorrelationId = null;

        // Act
        await interceptor
            .HandleAsync(
                message,
                (m, _) =>
                {
                    observedCorrelationId = m.CorrelationId;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(message.CorrelationId).IsEqualTo(httpId);
            _ = await Assert.That(observedCorrelationId).IsEqualTo(httpId);
        }
    }

    [Test]
    public async Task HandleAsync_AccessorHasCorrelationId_MessageHasEmptyCorrelationId_SetsCorrelationId(
        CancellationToken cancellationToken
    )
    {
        // Arrange — string.Empty is treated the same as null per IsNullOrEmpty contract.
        const string httpId = "http-correlation-id";
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(httpId);
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpCorrelationAccessor>();
        var field = accessor.GetType().GetField("_correlationId", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(accessor, httpId);

        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(scope.ServiceProvider);
        var message = new TestEvent { CorrelationId = string.Empty };

        // Act
        await interceptor.HandleAsync(message, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(message.CorrelationId).IsEqualTo(httpId);
    }

    [Test]
    public async Task HandleAsync_AccessorCorrelationIdIsEmpty_DoesNotModifyMessage(CancellationToken cancellationToken)
    {
        // Arrange
        var services = new ServiceCollection();
        _ = services
            .AddSingleton(Mock.Of<IHttpContextAccessor>().Object)
            .AddHttpCorrelation()
            .WithTestGenerator(string.Empty);
        var provider = services.BuildServiceProvider();

        var interceptor = new HttpCorrelationEventInterceptor<TestEvent>(provider);
        var message = new TestEvent { CorrelationId = null };

        // Act
        await interceptor.HandleAsync(message, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        // Assert
        _ = await Assert.That(message.CorrelationId).IsNull();
    }

    private sealed record TestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
